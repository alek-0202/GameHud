using GamesHud.Api.Palworld.ManagedConfiguration;

namespace GamesHud.Api.Tests;

public sealed class PalworldManagedConfigurationSerializerTests
{
    private readonly PalworldManagedConfigurationSerializer _serializer = new();

    [Fact]
    public void CanonicalOutputEscapesStringsAndMapsDurableHardToPalworldDifficult()
    {
        var expected = new PalworldManagedConfiguration(
            "Sérver \"A\" \\ end\\", "comma, and (parentheses)", 32, "Hard", "p\\\"ass", string.Empty);

        var content = _serializer.Serialize(expected);

        Assert.Equal(
            "[/Script/Pal.PalGameWorldSettings]\n" +
            "OptionSettings=(ServerName=\"Sérver \\\"A\\\" \\\\ end\\\\\",ServerDescription=\"comma, and (parentheses)\",ServerPlayerMaxNum=32,Difficulty=Difficult,ServerPassword=\"p\\\\\\\"ass\",AdminPassword=\"\")\n",
            content);
        Assert.True(_serializer.TryParse(content, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("None", "None")]
    [InlineData("Normal", "Normal")]
    [InlineData("Hard", "Difficult")]
    public void DifficultyMappingIsConfinedToPalworldBoundary(string durable, string palworld)
    {
        var content = _serializer.Serialize(Valid() with { Difficulty = durable });
        Assert.Contains($"Difficulty={palworld}", content, StringComparison.Ordinal);
        Assert.True(_serializer.TryParse(content, out var parsed));
        Assert.Equal(durable, parsed!.Difficulty);
    }

    [Theory]
    [InlineData("line\rbreak")]
    [InlineData("line\nbreak")]
    [InlineData("tab\tbreak")]
    [InlineData("nul\0break")]
    [InlineData("control\u001fbreak")]
    public void ControlCharactersFailBeforeSerialization(string unsafeValue)
    {
        var exception = Assert.Throws<PalworldManagedConfigurationException>(() =>
            _serializer.Serialize(Valid() with { ServerDescription = unsafeValue }));
        Assert.Equal(PalworldManagedConfigurationErrorCodes.IntentInvalid, exception.Code);
        Assert.DoesNotContain(unsafeValue, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyOptionalStringsAndAllowedExtremesRoundTrip()
    {
        var expected = Valid() with
        {
            ServerDescription = string.Empty,
            ServerPassword = string.Empty,
            AdminPassword = string.Empty,
            MaxPlayers = 1
        };
        Assert.True(_serializer.TryParse(_serializer.Serialize(expected), out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(ServerName=\"A\")\n")]
    [InlineData("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(ServerName=\"A\",ServerDescription=\"\",ServerPlayerMaxNum=32,Difficulty=Hard,ServerPassword=\"\",AdminPassword=\"\")\n")]
    [InlineData("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(ServerName=\"A\\q\",ServerDescription=\"\",ServerPlayerMaxNum=32,Difficulty=None,ServerPassword=\"\",AdminPassword=\"\")\n")]
    [InlineData("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(ServerName=\"A\",ServerName=\"B\",ServerDescription=\"\",ServerPlayerMaxNum=32,Difficulty=None,ServerPassword=\"\",AdminPassword=\"\")\n")]
    public void ParserRejectsIncompleteAliasesInvalidEscapesAndDuplicates(string content)
    {
        Assert.False(_serializer.TryParse(content, out _));
    }

    [Fact]
    public void ParserAcceptsSemanticPropertyReorderingAndCrLf()
    {
        var content = "[/Script/Pal.PalGameWorldSettings]\r\n" +
            "OptionSettings=(Difficulty=Normal, AdminPassword=\"\", ServerPlayerMaxNum=8, ServerName=\"Server\", ServerPassword=\"pw\", ServerDescription=\"Description\")\r\n";
        Assert.True(_serializer.TryParse(content, out var parsed));
        Assert.Equal(Valid() with { MaxPlayers = 8, Difficulty = "Normal", ServerPassword = "pw" }, parsed);
    }

    private static PalworldManagedConfiguration Valid() =>
        new("Server", "Description", 32, "None", string.Empty, string.Empty);
}

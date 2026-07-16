# GamesHud Architecture

## Vision

GamesHud is a generic web platform for managing Docker containers, game servers and infrastructure.

The project is designed to be extensible through plugins while keeping the Docker Core completely independent from any game-specific implementation.

Its long-term goal is to become a production-ready management platform rather than a tool dedicated to a single game.

---

# High Level Architecture

```text
                 +----------------+
                 |    Frontend    |
                 | React + Vite   |
                 +-------+--------+
                         |
                    HTTP / REST
                         |
                 +-------v--------+
                 |   .NET API     |
                 | Docker Core    |
                 +-------+--------+
                         |
                 Docker Engine API
                         |
                 +-------v--------+
                 | Docker Engine  |
                 +-------+--------+
                         |
        +----------------+----------------+
        |                |                |
   Palworld       Minecraft      Discord Bot
```

---

# Core Responsibilities

The Docker Core is responsible for generic infrastructure features only.

Examples:

- Containers
- Images
- Networks
- Volumes
- Logs
- Statistics
- Resource monitoring
- Backups
- Restore
- Scheduler

The Core must never contain game-specific logic.

---

# Plugin Responsibilities

Plugins are responsible for everything related to a specific application or game.

Examples:

Palworld Plugin

- Players
- Server settings
- RCON
- REST API
- Save management
- World information

Minecraft Plugin

- Properties
- Whitelist
- Players
- Worlds

Discord Plugin

- Status
- Commands
- Configuration

---

# Architecture Rules

These rules must always be respected.

## 1.

The Docker Core must remain completely independent from plugins.

## 2.

No game-specific logic may exist inside the Core.

## 3.

Controllers must never communicate directly with Docker SDK classes.

Always use dedicated services.

## 4.

External SDK models must never be exposed through the API.

Always map them to internal DTOs.

## 5.

Configuration must never be hardcoded.

Use:

- appsettings.json
- Environment Variables
- Secret storage

## 6.

Sensitive information must never be committed.

Examples:

- passwords
- tokens
- API keys
- VPS IPs
- Docker endpoints
- certificates

## 7.

All technical names must be written in English.

Including:

- files
- folders
- classes
- interfaces
- methods
- variables

---

# Current Docker Core Flow

The first implemented Docker Core flow is:

```text
React frontend
      |
      | GET /api/containers
      v
.NET API controller
      |
      | IContainerService
      v
Docker.DotNet client
      |
      | Docker Engine API
      v
Docker Engine containers
```

The API returns internal contracts only:

- `id`
- `name`
- `image`
- `state`
- `status`

Controllers must continue to depend on GamesHud services, not on Docker SDK types.
SDK models must stay inside the Docker service layer and be mapped before returning HTTP responses.

Docker access is configured through `Docker:Endpoint` or the `Docker__Endpoint` environment variable.
The future Linux VPS value is expected to be `unix:///var/run/docker.sock`.

Access to the Docker socket is high risk and must be restricted to the backend process only.
Frontend code must never access the Docker socket directly.

# Development Principles

The project follows these principles:

- Simplicity before abstraction.
- Build only what is needed.
- Avoid overengineering.
- Prefer composition over inheritance.
- Keep responsibilities small.
- Favor readability over clever code.
- Introduce abstractions only when duplication or complexity justifies them.

---

# Future Roadmap

## Docker Core

- Dashboard
- Containers
- Images
- Networks
- Volumes
- Logs
- Resource Usage
- Scheduler

## Plugins

- Palworld
- Minecraft
- Terraria
- Discord Bot
- Generic Docker

---

# Technology Stack

Backend

- .NET 8
- ASP.NET Core
- Docker Engine API

Frontend

- React
- Vite
- TypeScript

Infrastructure

- Docker
- Ubuntu
- Linux

---

# AI Development Rules

Any AI assisting this project must:

1. Read this document before implementing features.
2. Preserve the architecture.
3. Avoid unnecessary abstractions.
4. Avoid changing unrelated files.
5. Keep the Docker Core generic.
6. Never introduce game-specific logic into the Core.
7. Validate the project before considering the task complete.

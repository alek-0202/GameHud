# Frontend Guidelines

## Scope

These rules apply to the GamesHud React + Vite + TypeScript frontend.

---

## TypeScript

- Do not use `any`.
- Create project types for API contracts.
- Handle optional values explicitly.
- Avoid unnecessary casts.

---

## Components

- Components should have clear responsibilities.
- Avoid excessively large components.
- Do not split components only for aesthetics when there is no real benefit.
- Reuse existing patterns.

---

## API Access

- API communication should stay separate from presentation when appropriate.
- The API URL must come from `VITE_API_BASE_URL`.
- Do not hardcode VPS addresses.
- Do not place secrets in frontend code.

---

## UI States

Every query should consider, when applicable:

- loading
- success
- empty
- not found
- unavailable
- unexpected error

---

## Logs

- Treat logs as plain text.
- Do not use `dangerouslySetInnerHTML`.
- Preserve line breaks and readability.
- Do not execute or interpret received content.

---

## Lifecycle Actions

- Lifecycle controls belong in the container details view only.
- Do not add lifecycle buttons to the container list.
- Running containers may show stop and restart actions.
- Created, exited or stopped containers may show start actions.
- Stop and restart require a confirmation dialog.
- Start should be explicit but may run without a confirmation dialog.
- Disable lifecycle controls while an action is in progress for the current container.
- Show inline feedback after lifecycle actions.
- Refresh container details after successful lifecycle actions.
- Do not add batch, automatic or scheduled lifecycle actions without a new roadmap decision.

---

## Styling

- Keep the visual style consistent, simple and responsive.
- Do not add a large visual library without architectural alignment.
- Do not use emojis as primary indicators.
- Avoid unnecessary global styles.
- Preserve basic accessibility.

---

## State Management

- Prefer local state while it is sufficient.
- Do not add global state prematurely.
- Do not add React Router, TanStack Query, Axios or similar libraries without concrete need or prior alignment.

---

## Testing and Validation

- Evaluate whether tests are needed for relevant logic.
- Run `npm run build`.
- Run lint when a lint script exists.

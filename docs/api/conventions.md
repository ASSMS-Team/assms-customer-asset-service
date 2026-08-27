# API Conventions

Rules every endpoint in `assms-customer-asset-service` follows. They are written
down so a new endpoint looks like the existing ones and so the generated Swagger
page stays useful to QA without anyone having to read the controller.

## Routing

- Controllers declare their route explicitly and in lowercase:
  `[Route("api/customers")]`. Prefer this over `[Route("api/[controller]")]`,
  which takes its casing from the class name and produces `/api/Health`.
- The resource segment is a plural noun. No verbs in paths - the HTTP method
  carries the action.
- A single resource is addressed by its id as the last segment:
  `GET /api/customers/{id}`.

## Media types

- Every controller carries `[Produces("application/json")]`. This service speaks
  JSON and nothing else.
- Without it, ASP.NET Core advertises `application/json`, `text/json` and
  `text/plain` on every response, and Swagger UI shows all three in its response
  content-type dropdown. The attribute narrows that to `application/json`, so the
  dropdown offers exactly what the service actually returns.
- Request bodies are `application/json`. The `application/*+json` and `text/json`
  entries Swagger lists on the request body come from the default input
  formatters and are left as they are.

## JSON shape

- Property names are camelCase - the ASP.NET Core default.
- Dictionary keys are camelCase too. `Program.cs` sets
  `JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase` so that
  `ValidationProblemDetails.Errors` is keyed the same way property names are.
  Without it a DataAnnotations failure returns `"Name"` while a hand-built problem
  returns `"phone"`, and the frontend needs two rules.
- Timestamps are UTC and serialized in ISO 8601.

## Status codes

| Code | When |
| --- | --- |
| 200 | A read succeeded. |
| 201 | A resource was created. Set the `Location` header with `CreatedAtAction`. |
| 400 | The request failed validation. |
| 404 | The addressed resource does not exist. |
| 409 | The request is well-formed but conflicts with a business rule. |
| 503 | A dependency is unavailable (used by `GET /api/Health/db`). |

Do not return 200 with an error payload, and do not return 500 for a condition
the service can anticipate.

A resource *referenced* by a field in the body is not the *addressed* resource, so
a missing one is a 409 keyed on that field rather than a 404: `POST /api/assets`
carrying a `customerId` that matches no customer addresses the assets collection,
which exists - it is the field that is wrong, and keying it is what lets the
frontend render the message against that input.

## Errors

- All errors are RFC 7807 problem responses. Nothing hand-rolls its own error
  envelope.
- Validation failures are `ValidationProblemDetails`, keyed by field name so the
  frontend can render each message against the matching input.
- `[ApiController]` produces the 400 automatically from DataAnnotations before the
  action body runs, so actions contain no validation code - only branching on
  business rules.
- A business-rule conflict is returned as a `ValidationProblemDetails` keyed on the
  offending field with `Status` set explicitly, so a 409 lands on the same input as
  a 400 would:

  ```json
  {
    "title": "One or more validation errors occurred.",
    "status": 409,
    "errors": {
      "phone": ["An active customer already exists with this phone number."]
    }
  }
  ```

- `NotFound()` is likewise turned into a problem response by `[ApiController]`; do
  not attach a custom body to it.

## Validation

- Input rules live on the request DTO as DataAnnotations, not in the controller or
  the service.
- Constrain lengths to match the column widths in the migration, so a bad request
  fails as a 400 rather than as a database error.
- Enumerated values are validated with `[RegularExpression]` carrying a readable
  `ErrorMessage`, and mirror the `CHECK` constraint on the table.
- Rules that need the database - uniqueness, cross-row checks - belong in the
  service, and the database keeps the matching constraint. The service checks
  first for a clean error message; the unique index is what actually enforces it,
  because two concurrent requests can both pass the check before either inserts.

## DTOs

- Requests and responses are separate types. Never bind or return the entity.
- A request DTO exposes only client-supplied fields. Server-controlled values -
  id, status, audit timestamps, derived columns - are absent, because binding them
  from the body would be a mass-assignment hole.
- A response DTO omits internal mechanics that carry no business meaning
  (`phone_normalized`, `phone_active_unique`).
- Values the client supplied are echoed back as they were sent, not in their
  normalized form.

## Documentation

The project sets `GenerateDocumentationFile` and Swashbuckle reads the resulting
XML, so `///` comments are the API documentation - there is no second place to
update. `NoWarn` includes `1591`, so only the API surface has to be documented;
missing comments elsewhere are not build noise.

Every action carries:

- `<summary>` - what the endpoint does, in one or two sentences.
- `<param>` for **every** parameter. Documenting only some of them triggers
  CS1573, which `1591` does not suppress.
- `<response code="...">` for each status code, saying *when* it occurs, not what
  the number means. `409 when an active customer already has this phone number` is
  the point of the tag; `409 Conflict` is not.
- A `[ProducesResponseType]` for each status code, with the response type where
  there is a body. The attributes give Swagger the schema; the `<response>` tags
  give it the prose.

DTO properties carry `<summary>`, since those surface as field descriptions in the
schema view. State the constraint in the text where one exists ("Required, up to
100 characters"), because the reader is looking at the schema, not the annotations.

## Verifying a change

Swagger UI is served at `/swagger` in Development only. After changing an
endpoint, restart the service and confirm the page reflects the change - the raw
document is at `/swagger/v1/swagger.json`.

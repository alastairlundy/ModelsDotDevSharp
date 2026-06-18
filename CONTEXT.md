# CONTEXT

> Domain glossary for `ModelsDotDevSharp`. The C# type names that appear in the
> source code are *derived from* these terms; this file describes the domain, not
> the implementation.

---

## Provider

An AI vendor or service that serves models. Examples: `openai`, `anthropic`,
`google`, `mistral`. A provider is identified by a short string id and carries
display metadata (name, docs URL, SDK package, env-var keys, API URL). A
provider is **distinct from** the "data access" layer that fetches provider
information from models.dev — that layer is called a `Repository` (see below).

## Model — per-provider view

The shape of a model as it is served by a specific provider. Includes the
provider-specific facts: pricing, tiered cost overrides, alternative pricing
modes, experimental modes, request-body and header overrides, and the
provider-override sub-object (which controls how this particular model is
routed). The per-provider view **does not** include the model's intrinsic
context limits, weights, or benchmarks — those live on the canonical view.

## Model — canonical view

The provider-agnostic facts about a model: name, family, capability flags
(attachment, reasoning, tool calling, structured output, temperature),
knowledge-cutoff date, release date, last-updated date, open-weights flag,
modalities, **context limit**, **weights** (where to download them), and
**benchmarks**. The canonical view has no pricing data; it is "what the model
is", independent of who serves it.

## Provider override

A per-model delta over the top-level provider's routing defaults. The override
may change the SDK package, API URL, request shape (completions or responses),
request-body, and HTTP headers used to invoke this specific model. The override
is **not** a free-standing provider — it is always read in the context of the
top-level provider that owns the model. Two models in the same provider may
have different overrides.

## Context override

A dynamic-keyed tiered cost map on a model's pricing. Each entry's key is a
suffix string describing a context threshold (e.g., `context_over_200k`), and
the value is the cost that applies once the model's input context exceeds that
threshold. The convention is `"context_over_{N}"`; the key is a string, not a
nested structure, so the C# representation is a dictionary keyed by suffix.

## Mode

An alternative pricing mode for a model (e.g., `"fast"`, `"slow"`). Each mode
carries a cost that replaces the model's default cost when the mode is in
effect. The keys are arbitrary mode names, not nested structures; the C#
representation is a dictionary keyed by mode name.

## Experimental mode

A mode that is explicitly marked unstable by upstream. Carries the same shape
as a regular `Mode` but is surfaced under a separate `experimental` sub-object
on the model so that consumers can opt in (or not) deliberately.

## Interleaved reasoning

A model's strategy for interleaving reasoning content with its response. The
upstream representation is a boolean-or-object polymorphism: `true` means the
model supports interleaved reasoning in a general way; `{"field":
"reasoning_content"}` or `{"field": "reasoning_details"}` means it supports
interleaving with a specific field name. The two shapes are semantically
distinct — a consumer who cares about the field name must read the object
form, not just the boolean.

## Reasoning option

A polymorphic configuration entry for a model's reasoning behavior. Each
entry has a `type` discriminator: `toggle` (the model supports toggling
reasoning on/off), `budget_tokens` (the model supports a token budget, with
`min` and `max`), or `effort` (the model supports an effort level, with a
`values` list of allowed levels). The discriminator is a closed set.

## Modality

A mode of input or output a model supports. The closed set is `text`,
`image`, `audio`, `video`, `pdf`. A model declares its input modalities and
its output modalities as two independent lists.

## Status

A model's lifecycle status. The closed set is `alpha` (in alpha testing),
`beta` (in beta testing), or `deprecated` (no longer served). Absent means
the model is generally available.

## Logo

A provider's brand asset, served as an SVG. The asset is presentation, not
data: it is **not** fetched by this library, only referenced as a URL string.
The fallback is a default SVG served by the upstream when a provider has no
custom logo.

---

## Naming convention (informational)

For consistency, the data-access layer that fetches a single models.dev
endpoint uses the `Repository` suffix (e.g., the endpoint that fetches the
per-provider view, the endpoint that fetches the canonical view, etc.). The
`Provider` suffix is reserved for the AI vendor entity itself, not for
retrieval abstractions, because of the linguistic ambiguity that arose from
using the same word for two different concepts.

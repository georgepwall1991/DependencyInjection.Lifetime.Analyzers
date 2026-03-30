# Sample Verification

How sample diagnostics and sample-backed docs are expected to behave for this mission.

**What belongs here:** sample project roles, verifier expectations, stable anchors, docs/sample mapping facts.
**What does NOT belong here:** low-level analyzer implementation details.

---

## Sample Projects

- `samples/SampleApp`
  - Broad outward-facing sample matrix for `DI001` through `DI016`
  - Contains paired warning/safe examples under `samples/SampleApp/Diagnostics/*`
- `samples/DI015InAction`
  - Focused unresolved-dependency scenario showing broken vs fixed registrations

## Current Sample/Docs Wiring

- `tools/generate-growth-assets.mjs` owns `ruleSampleConfig`
- Rule pages and sample-backed content are derived from `samples/SampleApp/Diagnostics/*`
- During planning, missing snippet extraction was identified as a silent-drift risk

## Mission Expectations

- Sample verification should consume SARIF, not console text
- Stable matching should be based on rule ID, severity, and stable sample anchors
- The verifier may allow explicitly approved secondary diagnostics for overlapping sample cases
- Public outward-facing diagnostics must stay in parity with:
  - sample directories
  - sample/docs mappings
  - any approved alias/omission list used by the verifier

## Validation Notes

- Use temp directories/temp SARIF files
- Keep repository working tree clean after verification
- Prefer build-based validation over runtime execution for samples

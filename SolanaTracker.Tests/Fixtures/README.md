# Parser fixtures

These files contain the `result` object from the shape of `getTransaction`, not a JSON-RPC envelope. Fields unused by the extractor are omitted to keep the examples readable. They are not complete signed transactions and cannot be submitted to Solana.

- `successful-transfer.json`: a reduced version of the successful System Program transfer discussed in this learning project.
- `mixed-instructions.json`: a synthetic composite containing an encoded custom instruction, two top-level transfers, a Vote Program withdrawal, an account-creation instruction, and an inner transfer. Only top-level instruction indexes 1 and 4 should be extracted.

Tests mutate fresh copies to cover failures, invalid fields, alternative versions, and amount boundaries without making network calls.

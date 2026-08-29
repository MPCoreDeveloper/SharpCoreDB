# 11. Architecture Packages

> Optional NuGet packages that build production patterns on top of SharpCoreDB.
> Deep dives: [`docs/cqrs/README.md`](../cqrs/README.md) ·
> [`docs/distributed/README.md`](../distributed/README.md)

---

## 11.1 EventSourcing (`SharpCoreDB.EventSourcing`)

- Append-only per-stream storage, global ordered feed
- In-memory + persistent stores, snapshot policies
- Stream replay and projections-friendly reads

## 11.2 Projections (`SharpCoreDB.Projections`)

- Checkpoint persistence, OpenTelemetry-ready projection metrics
- Built for exactly-once-ish event handling loops
- See [`docs/internals/PROJECTIONS_OPEN_TELEMETRY_METRICS.md`](../internals/PROJECTIONS_OPEN_TELEMETRY_METRICS.md)

## 11.3 CQRS (`SharpCoreDB.CQRS`)

- Command/handler abstractions, aggregate root
- Outbox with dead-letter workflow for reliable side-effects
- Quick start: [`docs/cqrs/QUICKSTART.md`](../cqrs/QUICKSTART.md)

```csharp
public record CreateCustomerCommand(string Name, string Email) : ICommand;

public class CreateCustomerHandler : ICommandHandler<CreateCustomerCommand>
{
    private readonly IDatabase _db;
    public CreateCustomerHandler(IDatabase db) => _db = db;

    public Task<Unit> Handle(CreateCustomerCommand cmd, CancellationToken ct)
    {
        _db.Insert("customers",
            new Dictionary<string, object> { ["name"] = cmd.Name, ["email"] = cmd.Email });
        return Unit.Task;
    }
}
```

## 11.4 Distributed (`SharpCoreDB.Distributed`)

- Multi-master replication with vector clocks
- Distributed transactions (2PC)
- See [`docs/distributed/README.md`](../distributed/README.md) and
  [`docs/SHARPCOREDB_EMBEDDED_DISTRIBUTED_GUIDE.md`](../SHARPCOREDB_EMBEDDED_DISTRIBUTED_GUIDE.md)

## 11.5 Functional (`SharpCoreDB.Functional`)

`Option<T>`/`Fin<T>`/`Seq<T>` functional wrappers across adapters (see
[Providers](providers.md#103-functional-adapters)). Design rationale:
[`docs/NULLABLE_VS_OPTIONAL_REBUTTAL.md`](../NULLABLE_VS_OPTIONAL_REBUTTAL.md)

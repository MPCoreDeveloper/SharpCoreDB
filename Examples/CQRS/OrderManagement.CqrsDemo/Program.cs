// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using OrderManagement.CqrsDemo;
using SharpDispatch;

Console.WriteLine("========================================");
Console.WriteLine(" SharpCoreDB Explicit CQRS Demo");
Console.WriteLine(" Order Management (Command + Query Split)");
Console.WriteLine("========================================");
Console.WriteLine();

var writeRepository = new InMemoryOrderWriteRepository();
var readStore = new InMemoryOrderReadStore();
var projector = new OrderReadProjector(readStore);
var queryService = new OrderQueryService(readStore);

var dispatcher = new InMemoryCommandDispatcher();
dispatcher.RegisterHandler(new PlaceOrderCommandHandler(writeRepository, projector));
dispatcher.RegisterHandler(new AddOrderLineCommandHandler(writeRepository, projector));
dispatcher.RegisterHandler(new ConfirmOrderCommandHandler(writeRepository, projector));
dispatcher.RegisterHandler(new MarkOrderPaidCommandHandler(writeRepository, projector));

const string orderId = "CQRS-ORDER-001";

await DispatchAsync(
    dispatcher,
    new PlaceOrderCommand(
        OrderId: orderId,
        CustomerId: "CUST-900",
        Lines:
        [
            new OrderLineInput("PROD-1", "Laptop", 1, 1199.00m),
            new OrderLineInput("PROD-2", "Mouse", 1, 39.95m),
        ]),
    "PlaceOrder");

await DispatchAsync(
    dispatcher,
    new AddOrderLineCommand(
        OrderId: orderId,
        Line: new OrderLineInput("PROD-3", "Dock", 1, 149.00m)),
    "AddOrderLine");

await DispatchAsync(dispatcher, new ConfirmOrderCommand(orderId), "ConfirmOrder");
await DispatchAsync(dispatcher, new MarkOrderPaidCommand(orderId, "PAY-2026-001"), "MarkOrderPaid");

Console.WriteLine();
Console.WriteLine("=== Query side (read model) ===");
var summary = queryService.GetOrderSummary(new GetOrderSummaryQuery(orderId));
if (summary is null)
{
    Console.WriteLine("No read model found.");
}
else
{
    Console.WriteLine($"Order: {summary.OrderId}");
    Console.WriteLine($"Customer: {summary.CustomerId}");
    Console.WriteLine($"Status: {summary.Status}");
    Console.WriteLine($"Lines: {summary.LineCount}");
    Console.WriteLine($"Total: ${summary.TotalAmount:F2}");
    Console.WriteLine($"Payment Reference: {summary.PaymentReference}");
    Console.WriteLine($"Last Updated (UTC): {summary.UpdatedAtUtc:yyyy-MM-dd HH:mm:ss}");
}

Console.WriteLine();
Console.WriteLine("=== Write side (current command model state) ===");
var writeModel = writeRepository.GetRequired(orderId);
Console.WriteLine($"Version: {writeModel.Version}");
Console.WriteLine($"Status: {writeModel.Status}");
Console.WriteLine($"Line Count: {writeModel.Lines.Count}");
Console.WriteLine($"Total: ${writeModel.TotalAmount:F2}");

Console.WriteLine();
Console.WriteLine("=== CQRS vs Event Sourcing in this repository ===");
Console.WriteLine("CQRS demo (this project):");
Console.WriteLine("  - Splits command handling from query/read model.");
Console.WriteLine("  - Stores current write model state in memory for commands.");
Console.WriteLine("  - Updates a separate read model through projector notifications.");
Console.WriteLine();
Console.WriteLine("Event Sourcing demo (Examples/EventSourcing/OrderManagement):");
Console.WriteLine("  - Persists immutable event streams.");
Console.WriteLine("  - Rebuilds state by replaying events (optionally from snapshots).");
Console.WriteLine("  - Supports global event feed and point-in-time reconstruction.");

Console.WriteLine();
Console.WriteLine("Demo complete.");

static async Task DispatchAsync<TCommand>(
    InMemoryCommandDispatcher dispatcher,
    TCommand command,
    string operation,
    CancellationToken cancellationToken = default)
    where TCommand : ICommand
{
    var result = await dispatcher.DispatchAsync(command, cancellationToken);
    Console.WriteLine($"{operation}: {(result.Success ? "OK" : "FAILED")} {result.Message}");
}

// <copyright file="OrderCqrsDemo.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace OrderManagement.CqrsDemo;

using SharpDispatch;

/// <summary>
/// Command for creating an order on the write side.
/// </summary>
internal readonly record struct PlaceOrderCommand(string OrderId, string CustomerId, IReadOnlyList<OrderLineInput> Lines) : ICommand;

/// <summary>
/// Command for adding a line to an existing order on the write side.
/// </summary>
internal readonly record struct AddOrderLineCommand(string OrderId, OrderLineInput Line) : ICommand;

/// <summary>
/// Command for confirming an existing order.
/// </summary>
internal readonly record struct ConfirmOrderCommand(string OrderId) : ICommand;

/// <summary>
/// Command for marking an existing order as paid.
/// </summary>
internal readonly record struct MarkOrderPaidCommand(string OrderId, string PaymentReference) : ICommand;

/// <summary>
/// Query contract for retrieving read-model order summary data.
/// </summary>
internal readonly record struct GetOrderSummaryQuery(string OrderId);

/// <summary>
/// Input DTO used by command payloads.
/// </summary>
internal readonly record struct OrderLineInput(string ProductId, string ProductName, int Quantity, decimal UnitPrice);

internal enum OrderStatus
{
    Draft,
    Confirmed,
    Paid,
}

internal sealed class PlaceOrderCommandHandler(InMemoryOrderWriteRepository writeRepository, OrderReadProjector projector) : ICommandHandler<PlaceOrderCommand>
{
    private readonly InMemoryOrderWriteRepository _writeRepository = writeRepository;
    private readonly OrderReadProjector _projector = projector;

    public Task<CommandDispatchResult> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            return Task.FromResult(CommandDispatchResult.Fail("OrderId is required."));
        }

        if (string.IsNullOrWhiteSpace(command.CustomerId))
        {
            return Task.FromResult(CommandDispatchResult.Fail("CustomerId is required."));
        }

        if (command.Lines.Count == 0)
        {
            return Task.FromResult(CommandDispatchResult.Fail("At least one line is required."));
        }

        if (_writeRepository.Exists(command.OrderId))
        {
            return Task.FromResult(CommandDispatchResult.Fail($"Order '{command.OrderId}' already exists."));
        }

        var lines = command.Lines
            .Select(static line => new OrderLine(line.ProductId, line.ProductName, line.Quantity, line.UnitPrice))
            .ToList();

        var aggregate = new OrderWriteModel(
            command.OrderId,
            command.CustomerId,
            OrderStatus.Draft,
            lines,
            1,
            null);

        _writeRepository.Save(aggregate);
        _projector.Project(new OrderPlacedNotification(command.OrderId, command.CustomerId, aggregate.TotalAmount, aggregate.Lines.Count));

        return Task.FromResult(CommandDispatchResult.Ok("Order created."));
    }
}

internal sealed class AddOrderLineCommandHandler(InMemoryOrderWriteRepository writeRepository, OrderReadProjector projector) : ICommandHandler<AddOrderLineCommand>
{
    private readonly InMemoryOrderWriteRepository _writeRepository = writeRepository;
    private readonly OrderReadProjector _projector = projector;

    public Task<CommandDispatchResult> HandleAsync(AddOrderLineCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_writeRepository.TryGet(command.OrderId, out var order))
        {
            return Task.FromResult(CommandDispatchResult.Fail($"Order '{command.OrderId}' was not found."));
        }

        if (order.Status != OrderStatus.Draft)
        {
            return Task.FromResult(CommandDispatchResult.Fail("Only draft orders can be changed."));
        }

        order.Lines.Add(new OrderLine(command.Line.ProductId, command.Line.ProductName, command.Line.Quantity, command.Line.UnitPrice));
        order.Version++;
        _writeRepository.Save(order);

        _projector.Project(new OrderLineAddedNotification(command.OrderId, order.TotalAmount, order.Lines.Count));
        return Task.FromResult(CommandDispatchResult.Ok("Line added."));
    }
}

internal sealed class ConfirmOrderCommandHandler(InMemoryOrderWriteRepository writeRepository, OrderReadProjector projector) : ICommandHandler<ConfirmOrderCommand>
{
    private readonly InMemoryOrderWriteRepository _writeRepository = writeRepository;
    private readonly OrderReadProjector _projector = projector;

    public Task<CommandDispatchResult> HandleAsync(ConfirmOrderCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_writeRepository.TryGet(command.OrderId, out var order))
        {
            return Task.FromResult(CommandDispatchResult.Fail($"Order '{command.OrderId}' was not found."));
        }

        if (order.Status != OrderStatus.Draft)
        {
            return Task.FromResult(CommandDispatchResult.Fail("Only draft orders can be confirmed."));
        }

        order.Status = OrderStatus.Confirmed;
        order.Version++;
        _writeRepository.Save(order);

        _projector.Project(new OrderConfirmedNotification(command.OrderId));
        return Task.FromResult(CommandDispatchResult.Ok("Order confirmed."));
    }
}

internal sealed class MarkOrderPaidCommandHandler(InMemoryOrderWriteRepository writeRepository, OrderReadProjector projector) : ICommandHandler<MarkOrderPaidCommand>
{
    private readonly InMemoryOrderWriteRepository _writeRepository = writeRepository;
    private readonly OrderReadProjector _projector = projector;

    public Task<CommandDispatchResult> HandleAsync(MarkOrderPaidCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_writeRepository.TryGet(command.OrderId, out var order))
        {
            return Task.FromResult(CommandDispatchResult.Fail($"Order '{command.OrderId}' was not found."));
        }

        if (order.Status != OrderStatus.Confirmed)
        {
            return Task.FromResult(CommandDispatchResult.Fail("Only confirmed orders can be marked as paid."));
        }

        if (string.IsNullOrWhiteSpace(command.PaymentReference))
        {
            return Task.FromResult(CommandDispatchResult.Fail("Payment reference is required."));
        }

        order.Status = OrderStatus.Paid;
        order.PaymentReference = command.PaymentReference;
        order.Version++;
        _writeRepository.Save(order);

        _projector.Project(new OrderPaidNotification(command.OrderId, command.PaymentReference));
        return Task.FromResult(CommandDispatchResult.Ok("Order paid."));
    }
}

internal sealed class InMemoryOrderWriteRepository
{
    private readonly Dictionary<string, OrderWriteModel> _orders = [];

    public bool Exists(string orderId) => _orders.ContainsKey(orderId);

    public bool TryGet(string orderId, out OrderWriteModel order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        return _orders.TryGetValue(orderId, out order!);
    }

    public void Save(OrderWriteModel order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _orders[order.OrderId] = order;
    }

    public OrderWriteModel GetRequired(string orderId)
    {
        if (!TryGet(orderId, out var order))
        {
            throw new InvalidOperationException($"Order '{orderId}' was not found.");
        }

        return order;
    }
}

internal sealed class InMemoryOrderReadStore
{
    private readonly Dictionary<string, OrderReadModel> _orders = [];

    public OrderReadModel? Get(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        _orders.TryGetValue(orderId, out var model);
        return model;
    }

    public void Upsert(OrderReadModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _orders[model.OrderId] = model;
    }
}

internal sealed class OrderReadProjector(InMemoryOrderReadStore readStore)
{
    private readonly InMemoryOrderReadStore _readStore = readStore;

    public void Project(OrderPlacedNotification notification)
    {
        var now = DateTimeOffset.UtcNow;
        var readModel = new OrderReadModel(
            notification.OrderId,
            notification.CustomerId,
            OrderStatus.Draft,
            notification.LineCount,
            notification.TotalAmount,
            PaymentReference: null,
            UpdatedAtUtc: now);

        _readStore.Upsert(readModel);
    }

    public void Project(OrderLineAddedNotification notification)
    {
        var model = _readStore.Get(notification.OrderId)
            ?? throw new InvalidOperationException($"Read model for '{notification.OrderId}' was not found.");

        _readStore.Upsert(model with
        {
            LineCount = notification.LineCount,
            TotalAmount = notification.TotalAmount,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    public void Project(OrderConfirmedNotification notification)
    {
        var model = _readStore.Get(notification.OrderId)
            ?? throw new InvalidOperationException($"Read model for '{notification.OrderId}' was not found.");

        _readStore.Upsert(model with
        {
            Status = OrderStatus.Confirmed,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    public void Project(OrderPaidNotification notification)
    {
        var model = _readStore.Get(notification.OrderId)
            ?? throw new InvalidOperationException($"Read model for '{notification.OrderId}' was not found.");

        _readStore.Upsert(model with
        {
            Status = OrderStatus.Paid,
            PaymentReference = notification.PaymentReference,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
    }
}

internal sealed class OrderQueryService(InMemoryOrderReadStore readStore)
{
    private readonly InMemoryOrderReadStore _readStore = readStore;

    public OrderReadModel? GetOrderSummary(GetOrderSummaryQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.OrderId);
        return _readStore.Get(query.OrderId);
    }
}

internal sealed class OrderWriteModel(
    string orderId,
    string customerId,
    OrderStatus status,
    List<OrderLine> lines,
    long version,
    string? paymentReference)
{
    public string OrderId { get; } = orderId;

    public string CustomerId { get; } = customerId;

    public OrderStatus Status { get; set; } = status;

    public List<OrderLine> Lines { get; } = lines;

    public long Version { get; set; } = version;

    public string? PaymentReference { get; set; } = paymentReference;

    public decimal TotalAmount => Lines.Sum(static line => line.LineTotal);
}

internal readonly record struct OrderLine(string ProductId, string ProductName, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

internal readonly record struct OrderPlacedNotification(string OrderId, string CustomerId, decimal TotalAmount, int LineCount);

internal readonly record struct OrderLineAddedNotification(string OrderId, decimal TotalAmount, int LineCount);

internal readonly record struct OrderConfirmedNotification(string OrderId);

internal readonly record struct OrderPaidNotification(string OrderId, string PaymentReference);

internal sealed record class OrderReadModel(
    string OrderId,
    string CustomerId,
    OrderStatus Status,
    int LineCount,
    decimal TotalAmount,
    string? PaymentReference,
    DateTimeOffset UpdatedAtUtc);

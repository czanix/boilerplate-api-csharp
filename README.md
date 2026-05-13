# Czanix Boilerplate — API C# .NET

> Clean Architecture aplicada. Result Pattern para eliminar exceção de fluxo de negócio. OWASP desde o primeiro endpoint. Monitoramento embutido porque você não escala o que não vê.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![C#](https://img.shields.io/badge/C%23-14-239120?style=flat&logo=c-sharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16?style=flat&logo=postgresql&logoColor=white)](https://postgresql.org)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Tech Reference](https://img.shields.io/badge/Czanix-Tech%20Reference-gold)](https://czanix.com/pt/stack)

---

## Filosofia

**Não é Clean Architecture porque é moda. É porque:**

1. Você consegue testar a lógica de negócio sem subir banco ou servidor
2. Você troca o banco sem tocar em regra de negócio
3. Você lê um caso de uso e entende o que o sistema faz
4. Escala horizontal funciona porque a camada de domínio é stateless

Tudo aqui foi planejado para funcionar — não para funcionar por sorte.

---

## Estrutura de projetos

```
src/
├── Domain/                         # Zero dependências externas
│   ├── Entities/
│   │   └── Order.cs                # Entidade com invariantes protegidas
│   ├── Repositories/
│   │   └── IOrderRepository.cs     # Contrato — não implementação
│   ├── Services/
│   │   └── IPaymentGateway.cs      # Portas de saída
│   └── Common/
│       └── Result.cs               # Result<T> — sem exceção para negócio
│
├── Application/                    # Orquestra o domínio
│   ├── Orders/
│   │   ├── CreateOrder/
│   │   │   ├── CreateOrderCommand.cs
│   │   │   ├── CreateOrderHandler.cs   # Caso de uso
│   │   │   └── CreateOrderValidator.cs # Validação de entrada (FluentValidation)
│   │   └── CancelOrder/
│   │       ├── CancelOrderCommand.cs
│   │       └── CancelOrderHandler.cs
│   └── Common/
│       └── Behaviors/
│           ├── ValidationBehavior.cs   # MediatR pipeline — valida antes de executar
│           └── LoggingBehavior.cs      # Loga entrada/saída de cada command
│
├── Infrastructure/                 # Detalhes externos — banco, cache, email
│   ├── Persistence/
│   │   ├── AppDbContext.cs
│   │   ├── Migrations/
│   │   └── Repositories/
│   │       └── OrderRepository.cs  # Implementação do contrato do domínio
│   ├── Cache/
│   │   └── RedisCache.cs
│   └── ExternalServices/
│       └── StripePaymentGateway.cs # Implementa IPaymentGateway
│
└── Api/                            # Entry point HTTP
    ├── Controllers/
    │   └── OrdersController.cs
    ├── Middlewares/
    │   ├── ErrorHandlingMiddleware.cs  # Trata exceções globalmente
    │   └── RequestLoggingMiddleware.cs
    └── Program.cs                  # Composição da aplicação

tests/
├── Domain.Tests/           # Testa entidades e regras de negócio — sem banco
├── Application.Tests/      # Testa casos de uso com repositório mock
└── Integration.Tests/      # Testa com banco real (TestContainers)
```

---

## Result Pattern — o padrão que mais muda a qualidade do código

```csharp
// Domain/Common/Result.cs
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);

    // Implicit conversions para código fluido
    public static implicit operator Result<T>(T value) => Ok(value);
}

// Application/Orders/CancelOrder/CancelOrderHandler.cs
public class CancelOrderHandler : IRequestHandler<CancelOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(CancelOrderCommand command, CancellationToken ct)
    {
        var order = await _repository.GetByPublicIdAsync(command.OrderId);

        if (order is null)
            return Result<OrderDto>.Fail("ORDER_NOT_FOUND");     // negócio, não exceção

        if (order.Status == OrderStatus.Delivered)
            return Result<OrderDto>.Fail("ORDER_ALREADY_DELIVERED");

        order.Cancel();  // lógica na entidade — invariante protegida
        await _repository.SaveAsync(order);

        return Result<OrderDto>.Ok(OrderDto.From(order));
    }
}

// Api/Controllers/OrdersController.cs
[HttpDelete("{id}")]
public async Task<IActionResult> Cancel(string id)
{
    var result = await _mediator.Send(new CancelOrderCommand(id));

    return result.IsSuccess
        ? Ok(result.Value)
        : UnprocessableEntity(new { error = result.Error });
}
```

### Por que não `throw new BusinessException()`?

Exceção é para o inesperado — banco caiu, memória esgotada. "Pedido já entregue" é regra de negócio. Usar exceção para controle de fluxo:
- Esconde o caminho de erro no callstack
- Força o chamador a saber qual exceção capturar (acoplamento implícito)
- Não aparece no tipo de retorno — o compilador não ajuda você

---

## Entidade com invariantes — não um saco de dados

```csharp
// Domain/Entities/Order.cs
public class Order
{
    // Construtor privado — criação controlada
    private Order() { }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public OrderStatus Status { get; private set; }
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    private List<OrderItem> _items = new();

    // Fábrica estática — garante invariantes na criação
    public static Result<Order> Create(Customer customer, List<OrderItemDto> items)
    {
        if (!items.Any())
            return Result<Order>.Fail("ORDER_REQUIRES_ITEMS");

        if (customer.IsBlocked)
            return Result<Order>.Fail("CUSTOMER_BLOCKED");

        var order = new Order
        {
            PublicId = Guid.NewGuid(),
            Status = OrderStatus.Pending,
        };

        order._items.AddRange(items.Select(i => OrderItem.Create(i)));
        return Result<Order>.Ok(order);
    }

    // Comportamento na entidade — não no serviço
    public Result Cancel()
    {
        if (Status == OrderStatus.Delivered)
            return Result.Fail("CANNOT_CANCEL_DELIVERED");

        Status = OrderStatus.Cancelled;
        return Result.Ok();
    }
}
```

---

## Repository com interface — banco como detalhe

```csharp
// Domain/Repositories/IOrderRepository.cs — no domínio, sem referência a EF
public interface IOrderRepository
{
    Task<Order?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);
    Task<IEnumerable<Order>> GetActiveByCustomerAsync(long customerId, CancellationToken ct = default);
    Task SaveAsync(Order order, CancellationToken ct = default);
    Task DeleteAsync(Guid publicId, CancellationToken ct = default);
}

// Infrastructure/Persistence/Repositories/OrderRepository.cs
// A implementação concreta — detalhe de infraestrutura
public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public async Task<Order?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.PublicId == publicId && o.DeletedAt == null, ct);
    }
}
```

---

## Monitoramento — não é opcional, é o que permite escalar

```csharp
// Api/Program.cs — observabilidade na composição
builder.Services.AddHealthChecks()
    .AddNpgsql(connectionString, name: "postgres", tags: ["db"])
    .AddRedis(redisConfig, name: "redis", tags: ["cache"]);

// OpenTelemetry — rastreamento distribuído
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

// Structured logging — Serilog
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new JsonFormatter())        // JSON em produção
    .WriteTo.Seq(seqUrl)                         // ou qualquer SIEM
    .CreateLogger();
```

```csharp
// Application/Common/Behaviors/LoggingBehavior.cs
// Loga automaticamente TODA requisição MediatR — sem poluir o handler
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("Executing {Request}", requestName);
        var sw = Stopwatch.StartNew();

        var response = await next();
        sw.Stop();

        // Loga o tempo — identifica gargalos antes de virar problema
        _logger.LogInformation("Executed {Request} in {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);

        return response;
    }
}
```

**Por que monitorar desde o início?**

Você não escala o que não vê. Sem logs estruturados e health checks desde o primeiro deploy:
- Você vai escalar horizontal e o gargalo vai ser o banco (não o app)
- Você vai descobrir o problema pelo cliente
- Você vai gastar 3x mais em infraestrutura sem ganho real

Configure `/health`, Serilog e alertas antes de qualquer feature.

---

## Schema SQL incluído

```sql
-- Segue o padrão documentado em czanix.com/pt/stack/dados
CREATE TABLE orders (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    public_id   UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    customer_id BIGINT NOT NULL,
    status      NVARCHAR(20) NOT NULL DEFAULT 'pending',
    deleted_at  DATETIMEOFFSET NULL,
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT uq_orders_public_id UNIQUE (public_id)
);

-- NEWSEQUENTIALID() vs NEWID():
-- NEWID() = UUID completamente aleatório = fragmentação B-Tree
-- NEWSEQUENTIALID() = UUID ordenado por tempo = sem fragmentação
CREATE INDEX ix_orders_customer_active
    ON orders (customer_id, created_at DESC)
    WHERE deleted_at IS NULL;
```

---

## Architecture Decision Records (ADRs)

Decisões arquiteturais documentadas com contexto, motivo e trade-offs:

- [ADR-001: INT/BIGINT PK + UUID público](docs/adrs/001-bigint-pk-uuid-public.md)
- [ADR-002: Result Pattern vs Exceptions](docs/adrs/002-result-pattern-over-exceptions.md)
- [ADR-003: Clean Architecture com limites pragmáticos](docs/adrs/003-clean-architecture-boundaries.md)
- [ADR-004: Princípios de Modelagem de Dados](docs/adrs/004-database-design-principles.md)
- [ADR-005: Partitioning para Tabelas de Alto Volume](docs/adrs/005-table-partitioning.md)
- [ADR-006: Connection Pooling e Pool Sizing](docs/adrs/006-connection-pooling.md)
- [ADR-007: VACUUM, Autovacuum e Bloat Prevention](docs/adrs/007-vacuum-autovacuum.md)
- [ADR-008: Read Replicas e Separação de Leitura/Escrita](docs/adrs/008-read-replicas.md)
---

## Referência completa

- [czanix.com/pt/stack/backend](https://czanix.com/pt/stack/backend) — SOLID, Clean Architecture
- [czanix.com/pt/stack/dados](https://czanix.com/pt/stack/dados) — Padrões SQL
- [czanix.com/pt/stack/tradeoffs](https://czanix.com/pt/stack/tradeoffs) — Quando usar cada padrão

---

<div align="center">
<sub>Desenvolvido e mantido por <a href="https://czanix.com">Cesar Zanis</a> — Czanix</sub>
</div>

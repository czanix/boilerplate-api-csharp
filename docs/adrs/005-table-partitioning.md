# ADR-005: Partitioning para Tabelas de Alto Volume

## Status: Aceito
## Data: 2026-05-12

## Contexto

Tabelas que crescem indefinidamente (logs, eventos, transações, métricas) degradam com o tempo: queries ficam lentas, VACUUM leva horas, backups crescem sem controle. Partitioning resolve isso dividindo uma tabela lógica em várias tabelas físicas.

## Decisão

Usamos **partition by range** em tabelas de alto volume com acesso temporal.

```sql
-- Tabela particionada por mês
CREATE TABLE order_events (
    id          BIGINT GENERATED ALWAYS AS IDENTITY,
    order_id    INT NOT NULL,
    event_type  TEXT NOT NULL,
    payload     JSONB,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
) PARTITION BY RANGE (created_at);

-- Partições mensais (criar via cron ou migration)
CREATE TABLE order_events_2026_01 PARTITION OF order_events
    FOR VALUES FROM ('2026-01-01') TO ('2026-02-01');

CREATE TABLE order_events_2026_02 PARTITION OF order_events
    FOR VALUES FROM ('2026-02-01') TO ('2026-03-01');

-- Índice local por partição (criado automaticamente em cada partição)
CREATE INDEX ix_order_events_order ON order_events (order_id, created_at DESC);
```

## Quando particionar

| Cenário | Particionar? | Motivo |
|---------|-------------|--------|
| Tabela com > 10M rows e acesso temporal | **Sim** | Query planner faz partition pruning |
| Tabela de logs/eventos | **Sim** | Permite DROP PARTITION em vez de DELETE (instantâneo) |
| Tabela com < 1M rows | **Não** | Overhead de partitioning > benefício |
| Tabela acessada por PK (sem filtro temporal) | **Não** | Partition pruning não ajuda |

## Benefícios reais

1. **Partition pruning:** `WHERE created_at > '2026-04-01'` só acessa a partição de abril
2. **Manutenção:** VACUUM roda em partições pequenas, não na tabela inteira
3. **Retenção:** `DROP TABLE order_events_2025_01` é instantâneo. `DELETE FROM` trava a tabela
4. **Backup:** pode fazer backup parcial (só partições recentes)

## Cuidados

- **PK composta:** a partition key DEVE estar na PK → `PRIMARY KEY (id, created_at)`
- **FK:** PostgreSQL não suporta FK referenciando tabela particionada diretamente
- **Automação:** criar partições futuras via cron job ou pg_partman
- **Queries sem filtro temporal:** acessam TODAS as partições (sem pruning)

## Trade-offs

- Complexidade operacional (automação de criação de partições)
- PK composta (id + created_at) é menos intuitiva
- FK para tabelas particionadas requer workarounds

## Alternativas

- **TimescaleDB:** extensão que automatiza partitioning (boa para time-series)
- **Archiving:** mover dados antigos para tabela de histórico (mais simples)
- **Purge policy:** deletar dados antigos periodicamente (aceitável para logs)

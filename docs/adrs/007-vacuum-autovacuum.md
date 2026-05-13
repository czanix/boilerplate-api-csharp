# ADR-007: VACUUM, Autovacuum e Bloat Prevention

## Status: Aceito
## Data: 2026-05-12

## Contexto

PostgreSQL usa MVCC (Multi-Version Concurrency Control). Cada UPDATE ou DELETE não remove a row — cria uma nova versão e marca a antiga como "dead". Essas "dead tuples" ocupam espaço (bloat) e degradam performance de queries.

VACUUM é o processo que limpa dead tuples. Sem VACUUM adequado, tabelas com alto write crescem indefinidamente, queries ficam lentas, e o banco eventualmente para com "transaction wraparound".

## Como o PostgreSQL funciona

```
UPDATE orders SET status = 'confirmed' WHERE id = 42;

-- O que acontece internamente:
-- 1. Row original (id=42, status='pending') é marcada como DEAD
-- 2. Nova row (id=42, status='confirmed') é inserida
-- 3. Dead tuple ocupa espaço até VACUUM limpar

-- VACUUM limpa dead tuples e marca espaço como reutilizável
-- VACUUM FULL reescreve a tabela inteira (LOCK EXCLUSIVO — evitar em produção)
```

## Configuração de Autovacuum

```sql
-- postgresql.conf — configuração global
autovacuum = on                           -- NUNCA desligar
autovacuum_max_workers = 3                -- workers paralelos
autovacuum_naptime = 60                   -- intervalo entre verificações (segundos)

-- Thresholds padrão (quando o autovacuum dispara)
autovacuum_vacuum_threshold = 50          -- mínimo de dead tuples
autovacuum_vacuum_scale_factor = 0.2      -- 20% da tabela com dead tuples
autovacuum_analyze_threshold = 50
autovacuum_analyze_scale_factor = 0.1     -- 10% da tabela muda → re-analyze

-- Para tabelas de alto volume (>1M rows), o scale_factor de 20% é muito alto
-- 20% de 10M rows = 2M dead tuples antes de disparar vacuum
-- Solução: configurar por tabela
```

### Configuração por tabela (high-write)

```sql
-- Tabela de eventos com alto volume de escrita
ALTER TABLE order_events SET (
    autovacuum_vacuum_threshold = 1000,      -- dispara com 1000 dead tuples
    autovacuum_vacuum_scale_factor = 0.01,   -- OU 1% da tabela (o que vier primeiro)
    autovacuum_analyze_threshold = 500,
    autovacuum_analyze_scale_factor = 0.005
);

-- Tabela de logs — vacuum mais agressivo
ALTER TABLE audit_logs SET (
    autovacuum_vacuum_threshold = 500,
    autovacuum_vacuum_scale_factor = 0.005   -- 0.5% da tabela
);
```

## Monitoramento de bloat

```sql
-- Dead tuples por tabela (urgência de vacuum)
SELECT 
    schemaname || '.' || relname AS table,
    n_live_tup AS live_rows,
    n_dead_tup AS dead_rows,
    CASE WHEN n_live_tup > 0 
         THEN round(100.0 * n_dead_tup / n_live_tup, 1)
         ELSE 0 
    END AS dead_pct,
    last_autovacuum,
    last_autoanalyze
FROM pg_stat_user_tables
WHERE n_dead_tup > 1000
ORDER BY n_dead_tup DESC;

-- Tamanho real vs tamanho esperado (detecta bloat)
SELECT 
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname || '.' || tablename)) AS total_size,
    pg_size_pretty(pg_relation_size(schemaname || '.' || tablename)) AS table_size,
    pg_size_pretty(pg_indexes_size(schemaname || '.' || tablename)) AS index_size
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname || '.' || tablename) DESC;
```

## Transaction Wraparound — o killer silencioso

```sql
-- PostgreSQL usa transaction IDs (XIDs) de 32 bits → ~4 bilhões
-- Se o autovacuum não rodar, XIDs não são reciclados
-- Com ~2 bilhões de XIDs não reciclados, o banco para de aceitar writes

-- Monitorar urgência:
SELECT 
    datname,
    age(datfrozenxid) AS xid_age,
    CASE WHEN age(datfrozenxid) > 1500000000 THEN '🔴 CRÍTICO'
         WHEN age(datfrozenxid) > 1000000000 THEN '🟡 ATENÇÃO'
         ELSE '🟢 OK'
    END AS status
FROM pg_database
ORDER BY age(datfrozenxid) DESC;
```

## Checklist operacional

| Ação | Frequência | Comando |
|------|-----------|---------|
| Verificar dead tuples | Diário | Query `pg_stat_user_tables` |
| Verificar XID age | Semanal | Query `pg_database` |
| VACUUM ANALYZE manual | Após bulk loads | `VACUUM ANALYZE orders;` |
| Reindex | Mensal (se necessário) | `REINDEX INDEX CONCURRENTLY ix_orders_customer;` |
| VACUUM FULL | **Raramente** | Lock exclusivo — agendar em janela de manutenção |

## O que NUNCA fazer

- ❌ Desligar autovacuum ("tá usando CPU")
- ❌ VACUUM FULL em produção sem janela de manutenção
- ❌ Ignorar alertas de XID age > 1 bilhão
- ❌ Configurar scale_factor alto em tabelas grandes (default 0.2 é ruim para >1M rows)

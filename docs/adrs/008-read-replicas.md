# ADR-008: Read Replicas e Separação de Leitura/Escrita

## Status: Proposto (implementar quando necessário)
## Data: 2026-05-12

## Contexto

Quando o banco de dados atinge o limite de um único servidor (CPU saturada, I/O no limite), a primeira ação não é sharding — é **separar leituras de escritas**. A maioria dos sistemas é 80-90% leitura.

## Decisão

Separar em **primary** (escrita) e **replica** (leitura) quando a carga justificar.

### Arquitetura

```
                    ┌─────────────┐
                    │   App Layer │
                    └──────┬──────┘
                           │
                    ┌──────┴──────┐
                    │  Router     │
                    │  (código)   │
                    └──┬──────┬───┘
                       │      │
              Write    │      │  Read
                       ▼      ▼
                ┌──────────┐ ┌──────────┐
                │ Primary  │ │ Replica  │
                │ (RW)     │→│ (RO)     │ streaming replication
                └──────────┘ └──────────┘
```

### Implementação no código

```typescript
// connection.ts — duas fontes de dados
const writePool = new Pool({ connectionString: PRIMARY_URL });
const readPool = new Pool({ connectionString: REPLICA_URL });

// Repository pattern — já preparado
class PgOrderRepository implements OrderRepository {
  // Write: sempre no primary
  async save(order: Order): Promise<void> {
    await writePool.query('INSERT INTO orders ...', [...]);
  }

  // Read: pode ir na replica
  async findByPublicId(publicId: string): Promise<Order | null> {
    const result = await readPool.query('SELECT * FROM orders WHERE public_id = $1', [publicId]);
    return result.rows[0] ? Order.fromPersistence(result.rows[0]) : null;
  }

  // Read que precisa de consistência forte: vai no primary
  async findByIdForUpdate(id: number): Promise<Order | null> {
    const result = await writePool.query(
      'SELECT * FROM orders WHERE id = $1 FOR UPDATE',
      [id]
    );
    return result.rows[0] ? Order.fromPersistence(result.rows[0]) : null;
  }
}
```

## Replication lag

```
⚠️  Streaming replication tem lag (geralmente < 100ms, pode chegar a segundos)

Cenário problemático:
1. User cria pedido (write → primary)
2. User é redirecionado para tela de detalhes (read → replica)
3. Replica ainda não recebeu o INSERT → 404!

Soluções:
A) "Read your own writes" — após write, próximo read vai no primary
B) Stickiness temporal — 5 segundos após write, reads vão no primary
C) Versão otimista — app usa dados locais enquanto replica sincroniza
```

### Monitoramento de lag

```sql
-- Na replica: verificar lag em bytes
SELECT 
    pg_wal_lsn_diff(
        pg_last_wal_receive_lsn(), 
        pg_last_wal_replay_lsn()
    ) AS replay_lag_bytes,
    extract(epoch from now() - pg_last_xact_replay_timestamp()) AS replay_lag_seconds;

-- No primary: verificar status das replicas
SELECT 
    client_addr,
    state,
    sent_lsn,
    write_lsn,
    flush_lsn,
    replay_lsn,
    pg_wal_lsn_diff(sent_lsn, replay_lsn) AS lag_bytes
FROM pg_stat_replication;
```

## Quando usar read replicas

| Cenário | Replica? | Motivo |
|---------|---------|--------|
| CPU do banco > 70% com maioria de SELECTs | **Sim** | Descarregar leitura |
| Relatórios pesados (BI, analytics) | **Sim** | Não travar o primary |
| Aplicação com < 100 req/s | **Não** | Um PostgreSQL aguenta muito mais |
| Precisa de consistência forte em todas as queries | **Não** | Lag é inevitável |

## Quando NÃO é a resposta

- **Writes são o bottleneck:** replica não ajuda (todas as escritas vão no primary)
- **Query individual é lenta:** problema é a query, não o servidor. Use `EXPLAIN ANALYZE`
- **Tabela sem índice:** replica vai ser lenta também. Corrija o índice
- **Lock contention:** replica não resolve — revise a lógica de transação

## Alternativas antes de replicar

1. **Otimizar queries** — 80% dos problemas são queries sem índice
2. **Cache (Redis)** — dados que não mudam a cada request
3. **Materialized views** — dados pré-computados para dashboards
4. **Vertical scaling** — mais CPU/RAM é mais simples que replica
5. **Connection pooling** — PgBouncer reduz overhead de conexões

## Ordem correta de escalabilidade

```
1. Otimizar queries (EXPLAIN ANALYZE)
2. Adicionar índices corretos
3. Configurar connection pooling
4. Adicionar cache (Redis)
5. Read replicas                  ← você está aqui
6. Partitioning
7. Sharding (último recurso)
```

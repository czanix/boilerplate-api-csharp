# ADR-006: Connection Pooling e Pool Sizing

## Status: Aceito
## Data: 2026-05-12

## Contexto

Cada conexão com PostgreSQL consome ~10MB de RAM no servidor. Uma aplicação com 100 instâncias abrindo 20 conexões cada = 2.000 conexões = 20GB de RAM só para connections. Sem pool ou com pool mal configurado, o banco morre de exaustão.

## Decisão

### Pool na aplicação

Toda aplicação DEVE usar connection pool com limites explícitos.

```
# Node.js (pg)
const pool = new Pool({
    max: 20,                    # conexões máximas por instância
    idleTimeoutMillis: 30000,   # fecha conexão ociosa após 30s
    connectionTimeoutMillis: 5000, # timeout para obter conexão do pool
});

# Python (SQLAlchemy)
engine = create_async_engine(
    DATABASE_URL,
    pool_size=10,               # conexões permanentes
    max_overflow=10,            # extras temporárias
    pool_timeout=5,             # timeout para obter conexão
    pool_recycle=1800,          # recicla conexão a cada 30min
)

# Go (pgx)
config.MaxConns = 20
config.MinConns = 5
config.MaxConnLifetime = 30 * time.Minute
config.MaxConnIdleTime = 5 * time.Minute

# C# (EF Core / Npgsql)
"Pooling=true;Minimum Pool Size=5;Maximum Pool Size=20;Connection Idle Lifetime=300"

# Java (HikariCP — Spring Boot default)
spring.datasource.hikari.maximum-pool-size=20
spring.datasource.hikari.minimum-idle=5
spring.datasource.hikari.idle-timeout=300000
spring.datasource.hikari.max-lifetime=1800000
```

### Fórmula de pool sizing

```
pool_size = (cores_do_banco * 2) + número_de_discos_efetivos

# Exemplo: banco com 4 cores e SSD (1 disco efetivo)
pool_size = (4 * 2) + 1 = 9 conexões por instância
```

**Regra prática:** para a maioria dos cenários, **10-20 conexões por instância** é o sweet spot.

### PgBouncer (quando necessário)

```
# pgbouncer.ini
[databases]
mydb = host=127.0.0.1 port=5432 dbname=mydb

[pgbouncer]
pool_mode = transaction          # libera conexão após cada transação
max_client_conn = 1000           # aceita até 1000 conexões de clientes
default_pool_size = 20           # mantém 20 conexões reais no banco
reserve_pool_size = 5
reserve_pool_timeout = 3
```

**Quando usar PgBouncer:**
- Mais de 5 instâncias da aplicação
- Serverless (cada request = nova conexão)
- Migrações com muitas conexões temporárias

### Pool modes

| Mode | Libera conexão quando | Usar quando |
|------|----------------------|-------------|
| **session** | Cliente desconecta | Aplicações com sessão longa |
| **transaction** | Transação termina | **Padrão recomendado** — APIs REST |
| **statement** | Statement termina | Apenas queries simples (sem transação multi-statement) |

## Health check

```sql
-- Query de health check do pool (não use SELECT 1 — não valida nada)
SELECT pg_is_in_recovery();  -- detecta se é primary ou replica
```

## Monitoramento

```sql
-- Conexões ativas vs limite
SELECT 
    count(*) as active,
    (SELECT setting FROM pg_settings WHERE name = 'max_connections') as max
FROM pg_stat_activity
WHERE state != 'idle';

-- Conexões por aplicação
SELECT application_name, count(*) 
FROM pg_stat_activity 
GROUP BY application_name 
ORDER BY count(*) DESC;
```

## Trade-offs

- Pool muito grande: consome RAM do banco desnecessariamente
- Pool muito pequeno: aplicação espera por conexão (timeout)
- PgBouncer adiciona latência (~1ms) e complexidade operacional

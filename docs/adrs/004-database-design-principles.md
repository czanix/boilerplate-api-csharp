# ADR-004: Princípios de Modelagem de Dados

## Status: Aceito
## Data: 2026-05-12

## Contexto

A maioria dos projetos negligencia a modelagem de dados. O código muda fácil; o banco não. Uma decisão errada de modelagem pode custar meses de refatoração, downtime e perda de dados.

Este ADR documenta as decisões de modelagem que usamos em produção.

## Decisões

### 1. Tipos de dados corretos

```sql
-- ✅ Correto
price       NUMERIC(12, 2)    -- dinheiro NUNCA é float
quantity    INTEGER           -- nunca SMALLINT (vai faltar espaço cedo ou tarde)
email       TEXT              -- VARCHAR sem limite é TEXT no PostgreSQL (mesmo custo)
status      TEXT              -- com CHECK constraint (não ENUM — impossível alterar sem migration)
created_at  TIMESTAMPTZ       -- SEMPRE com timezone. Sem timezone = bug futuro garantido
cpf         CHAR(11)          -- tamanho fixo = CHAR. Variável = TEXT
percentage  NUMERIC(5, 2)     -- 100.00 max, 2 casas. Nunca float

-- ❌ Errado
price       FLOAT             -- 0.1 + 0.2 = 0.30000000000000004
quantity    SMALLINT          -- "nunca vai passar de 32K" (vai)
email       VARCHAR(255)      -- limite arbitrário, mesmo custo que TEXT no Postgres
status      my_status_enum    -- ALTER TYPE é DDL lock. TEXT + CHECK é flexível
created_at  TIMESTAMP         -- sem timezone: 15:00 é BRT? UTC? Ninguém sabe
```

### 2. Soft Delete — nunca delete dados

```sql
-- Toda tabela de negócio tem deleted_at
deleted_at  TIMESTAMPTZ NULL  -- NULL = ativo, preenchido = "deletado"

-- Índice filtrado: só indexa registros ativos
CREATE INDEX ix_customers_email_active
    ON customers (email)
    WHERE deleted_at IS NULL;
```

**Motivo:** dados são o ativo mais valioso. Deletar é irreversível. Soft delete permite:
- Auditoria: "quem deletou, quando?"
- Recuperação: "desfaz essa deleção"
- Analytics: "quantos clientes cancelaram no Q1?"

### 3. Auditoria mínima: created_at + updated_at

```sql
created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
```

Toda tabela de negócio tem esses dois campos. `updated_at` é atualizado via trigger ou application layer.

### 4. Índices intencionais

```sql
-- ✅ Índice que serve uma query específica
CREATE INDEX ix_orders_customer_active
    ON orders (customer_id, created_at DESC)
    WHERE deleted_at IS NULL;

-- ✅ Índice para busca textual (PostgreSQL)
CREATE INDEX ix_products_name_search
    ON products USING gin (to_tsvector('portuguese', name));

-- ❌ Índice "por via das dúvidas" em toda coluna
-- Cada índice custa write performance. Crie sob demanda, não "por precaução"
```

**Regra:** índice existe para servir uma query. Sem query, sem índice. Use `EXPLAIN ANALYZE` antes de criar.

### 5. Constraints no banco, não só no código

```sql
-- O banco é a última linha de defesa
CONSTRAINT chk_orders_status CHECK (status IN ('pending', 'confirmed', 'cancelled', 'delivered'))
CONSTRAINT chk_items_quantity CHECK (quantity > 0)
CONSTRAINT chk_items_price CHECK (unit_price >= 0)
CONSTRAINT uq_customers_email UNIQUE (email) -- se unique, tem que ter constraint
```

**Motivo:** código falha, bugs acontecem, deploys errados existem. O banco NUNCA pode aceitar dado inconsistente, independente do que a aplicação enviar.

### 6. Foreign Keys — SEMPRE

```sql
-- FK é inegociável em dados relacionais
order_id BIGINT NOT NULL REFERENCES orders(id)
-- ou INT para tabelas de volume normal
order_id INT NOT NULL REFERENCES orders(id)
```

**Sem FK = grafo de dados quebrado.** "A gente faz a validação no código" não é válido — código tem bug, FK não.

### 7. Nomenclatura

| Padrão | Exemplo | Motivo |
|--------|---------|--------|
| **snake_case** | `customer_id` | Padrão SQL, funciona sem aspas |
| **Tabela no plural** | `orders` | Uma tabela contém N registros |
| **PK = `id`** | `orders.id` | Simples, consistente |
| **FK = tabela_singular_id** | `order_id` | Explícito, sem ambiguidade |
| **Índice = ix_tabela_colunas** | `ix_orders_customer_active` | Identificável no EXPLAIN |
| **Constraint = prefixo_tabela_desc** | `chk_orders_status` | uq_, chk_, fk_ |

### 8. TIMESTAMPTZ — sempre com timezone

```sql
-- ✅ PostgreSQL: TIMESTAMPTZ armazena em UTC, converte na leitura
-- ✅ SQL Server: DATETIMEOFFSET armazena offset junto

-- Na aplicação: SEMPRE armazene em UTC
-- Na exibição: converta para o timezone do usuário
```

**Bug clássico:** sistema em BRT grava `2026-05-12 15:00` sem timezone. Servidor migra para região em UTC. Agora `15:00` é interpretado como UTC e vira `12:00 BRT`. Dados corrompidos retroativamente.

## Trade-offs assumidos

- **Soft delete ocupa espaço:** sim, mas storage é barato e dados são caros
- **Constraints desaceleram writes:** sim, mas dados inconsistentes desaceleram o negócio
- **Índices filtrados são PostgreSQL-specific:** aceito. Se migrar de banco, reescrevemos índices

## Quando flexibilizar

- **Tabelas de log/eventos:** não precisam de soft delete (já são append-only)
- **Tabelas temporárias:** não precisam de constraints elaboradas
- **Cache tables:** podem ter modelagem mais simples (dados descartáveis)

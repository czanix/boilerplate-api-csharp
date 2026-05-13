# ADR-001: INT/BIGINT PK + UUID Público

## Status: Aceito
## Data: 2026-05-12

## Contexto

Toda tabela precisa de uma primary key. As opções mais comuns são:
- **UUID como PK:** Simples, mas índices B-tree com UUID são 4x maiores que INT
- **INT/BIGINT auto-increment:** Performance excelente, mas expõe sequência na API
- **INT/BIGINT PK + UUID público:** Performance no banco + segurança na API

## Decisão

Usamos **INT ou BIGINT como PK interna** (dependendo do volume) e **UUID como identificador público**.

```sql
-- Tabelas com volume normal (< 2 bilhões de registros)
CREATE TABLE customers (
    id        INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,       -- 4 bytes
    public_id UUID NOT NULL DEFAULT gen_random_uuid(),
    CONSTRAINT uq_customers_public_id UNIQUE (public_id)
);

-- Tabelas de alto volume (logs, eventos, transações)
CREATE TABLE order_events (
    id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,    -- 8 bytes
    public_id UUID NOT NULL DEFAULT gen_random_uuid(),
    CONSTRAINT uq_order_events_public_id UNIQUE (public_id)
);
```

## Quando usar INT vs BIGINT

| Tipo | Tamanho | Limite | Usar quando |
|------|---------|--------|-------------|
| **INT** | 4 bytes | ~2.1 bilhões | Maioria das tabelas: users, orders, products, categories |
| **BIGINT** | 8 bytes | ~9.2 quintilhões | Tabelas de alto volume: events, logs, metrics, IoT data |

**Regra prática:** comece com INT. Se a tabela pode crescer além de centenas de milhões de registros, use BIGINT. Na dúvida, INT basta — migrar de INT para BIGINT é trivial.

## Motivo

1. **Performance:** INT ocupa 4 bytes, UUID ocupa 16. Joins e índices são 4x menores
2. **Segurança:** API só expõe UUID. Ninguém consegue estimar volume (id=1, id=2, id=3...)
3. **Compatibilidade:** Funciona em qualquer banco relacional
4. **Foreign keys:** Sempre via INT/BIGINT (performático), nunca UUID
5. **Pragmatismo:** Não gaste 8 bytes (BIGINT) onde 4 bytes (INT) resolvem

## Trade-off

- Complexidade: duas colunas em vez de uma
- O modelo e o repository precisam mapear `publicId` para `id` interno
- Decisão extra: "essa tabela é INT ou BIGINT?"

## Alternativas Rejeitadas

- **UUID como PK:** Índice B-tree fragmenta com inserções aleatórias, degradando performance em tabelas grandes (>1M rows)
- **BIGINT exposto na API:** Vulnerabilidade IDOR — atacante enumera `/api/orders/1`, `/api/orders/2`
- **BIGINT para tudo:** Desperdício — 99% das tabelas nunca chegam perto de 2 bilhões de registros

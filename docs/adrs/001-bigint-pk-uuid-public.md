# ADR-001: BIGINT PK + UUID Público

## Status: Aceito
## Data: 2026-05-12

## Contexto

Toda tabela precisa de uma primary key. As opções mais comuns são:
- **UUID como PK:** Simples, mas índices B-tree com UUID são 4x maiores que BIGINT
- **BIGINT auto-increment:** Performance excelente, mas expõe sequência na API
- **BIGINT PK + UUID público:** Performance no banco + segurança na API

## Decisão

Usamos **BIGINT como PK interna** e **UUID como identificador público**.

```sql
CREATE TABLE orders (
    id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,  -- interno
    public_id UUID NOT NULL DEFAULT gen_random_uuid(),           -- API
    CONSTRAINT uq_orders_public_id UNIQUE (public_id)
);
```

## Motivo

1. **Performance:** BIGINT ocupa 8 bytes, UUID ocupa 16. Joins e índices são 2x menores
2. **Segurança:** API só expõe UUID. Ninguém consegue estimar volume (id=1, id=2, id=3...)
3. **Compatibilidade:** Funciona em qualquer banco relacional
4. **Foreign keys:** Sempre via BIGINT (performático), nunca UUID

## Trade-off

- Complexidade: duas colunas em vez de uma
- O modelo e o repository precisam mapear `publicId` para `id` interno

## Alternativas Rejeitadas

- **UUID como PK:** Índice B-tree fragmenta com inserções aleatórias, degradando performance em tabelas grandes (>1M rows)
- **BIGINT exposto na API:** Vulnerabilidade IDOR — atacante enumera `/api/orders/1`, `/api/orders/2`

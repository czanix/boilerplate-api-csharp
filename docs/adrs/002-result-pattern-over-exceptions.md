# ADR-002: Result Pattern em vez de Exceptions

## Status: Aceito
## Data: 2026-05-12

## Contexto

Em qualquer aplicação, existem dois tipos de "erro":
1. **Inesperado:** banco caiu, memória cheia, timeout de rede
2. **Esperado:** email duplicado, estoque insuficiente, pedido sem itens

A maioria dos projetos usa `try/catch` para ambos. Isso mistura fluxo de negócio com erros de infraestrutura.

## Decisão

Usamos **Result Pattern** para erros de negócio e reservamos exceções para o genuinamente inesperado.

```
// Result Pattern — explícito, tipado, forçado pelo compilador
function createOrder(input): Result<Order> {
  if (items.length === 0) return fail("Pedido sem itens");  // negócio
  return ok(order);                                          // sucesso
}

// Exception — só para o inesperado
await db.query(sql);  // pode lançar se o banco cair — isso SIM é exceção
```

## Motivo

1. **Clareza:** O chamador vê que precisa lidar com sucesso E falha
2. **Performance:** Exceções são caras (stack trace), Result é uma struct/type
3. **Testabilidade:** `expect(result.ok).toBe(false)` é mais claro que `expect(() => ...).toThrow()`
4. **Composição:** Results podem ser encadeados (flatMap, match, fold)

## Trade-off

- Verbosidade: mais código no happy path vs try/catch
- Curva de aprendizado para times acostumados com exceções

## Alternativas Rejeitadas

- **Exceptions para tudo:** Mistura fluxo de negócio com erro de infra. `catch` vira lixeira.
- **Error codes numéricos:** Frágil, sem tipagem, fácil de esquecer de checar

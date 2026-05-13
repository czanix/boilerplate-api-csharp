# ADR-003: Clean Architecture com Limites Pragmáticos

## Status: Aceito
## Data: 2026-05-12

## Contexto

Clean Architecture define 4 camadas:
1. **Domain:** Entidades e regras de negócio puras
2. **Application:** Casos de uso que orquestram domínio + infra
3. **Infrastructure:** Banco, cache, APIs externas
4. **Presentation:** HTTP controllers, CLI, etc.

A regra de dependência é: camadas internas NÃO conhecem camadas externas.

## Decisão

Adotamos Clean Architecture com **pragmatismo**:
- Domain é 100% puro — zero import de framework
- Repository é uma **interface** no domínio, implementação em infrastructure
- Use cases retornam **Result<T>**, não lançam exceções
- Controllers são finos — só convertem HTTP ↔ Use Case

## Limites Pragmáticos

1. **Não criamos abstrações sem necessidade.** Se só existe um banco e não vai mudar, o repository impl pode ser simples
2. **Não separamos em múltiplos projetos/packages** se o time é pequeno. Pastas bastam
3. **Injeção de dependência manual** — sem container IoC quando o grafo é simples

## Trade-off

- Mais arquivos e indireção vs projeto monolítico simples
- DI manual pode ficar verbosa com muitas dependências

## Quando NÃO usar

- Protótipo ou MVP descartável (< 3 meses de vida)
- Script one-off ou automação simples
- Projeto com 1 desenvolvedor e < 5 endpoints

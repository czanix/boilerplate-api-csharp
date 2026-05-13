# ADR-009: Observabilidade Distribuída e Testes de Carga

## Status: Aceito
## Data: 2026-05-12

## Contexto

Uma arquitetura não pode ser chamada de "produção" baseando-se apenas em testes de unidade e achismos. Sem observabilidade, microsserviços viram caixas pretas. Sem testes de carga, o comportamento sob estresse é uma loteria. Padrões de mercado (Vercel, Stripe, Nubank) tratam isso como requisito fundamental, não como melhoria futura.

## Decisão

1. **Testes de Carga (k6):** Todo serviço crítico deve ter um baseline de performance validado usando [k6](https://k6.io/). O SLA de latência exigido é p(95) < 200ms.
2. **Distributed Tracing (OpenTelemetry + Jaeger):** Todo request HTTP, query de banco de dados e chamada de serviço externo deve gerar um *Span* com o mesmo *Trace ID*.
3. **Centralização de Logs (Seq/ELK):** Logs no console não escalam. Usamos Structured Logging (JSON) injetando o *Trace ID* em cada entrada.
4. **DevContainers:** Disponibilizamos um ambiente de desenvolvimento reproduzível garantindo que "funciona na minha máquina" não seja mais um problema.

## Implementação

- O `docker-compose.yml` local deve subir o Jaeger para análise visual imediata dos gargalos (tracing).
- Os scripts de validação e stress test do k6 ficam consolidados na pasta `tests/load/`.
- Integração contínua (CI) é configurada para falhar o build caso as regressões de performance não atinjam a meta de p(95).

## Consequências

- **Aumento de confiança:** Deploy na sexta-feira às 18h torna-se um não-evento.
- **Troubleshooting imediato:** Um erro 500 mostra instantaneamente qual query ou serviço downstream falhou via visualização de tracing.
- **Previsibilidade de escala:** A engenharia comprova o limite exato de throughput e sabe o momento exato de escalar pods ou partições do banco.

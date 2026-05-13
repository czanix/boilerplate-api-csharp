import http from 'k6/http';
import { check, sleep } from 'k6';

// Comando de execução: k6 run tests/load/k6-script.js
export const options = {
  stages: [
    { duration: '10s', target: 50 },  // Ramp-up (sobe para 50 VUs)
    { duration: '30s', target: 50 },  // Sustenta 50 VUs em carga
    { duration: '10s', target: 0 },   // Ramp-down
  ],
  thresholds: {
    http_req_duration: ['p(95)<200'], // 95% das requests DEVEM ser < 200ms
    http_req_failed: ['rate<0.01'],   // Falha máxima tolerável: 1%
  },
};

export default function () {
  // Ajuste a porta dependendo do microsserviço
  const targetUrl = __ENV.API_URL || 'http://localhost:3000';
  
  // Health check target (Caminho feliz para validar infra)
  const res = http.get(`${targetUrl}/health`);
  
  check(res, {
    'status is 200': (r) => r.status === 200,
    'latency is optimal': (r) => r.timings.duration < 200,
  });

  sleep(1);
}

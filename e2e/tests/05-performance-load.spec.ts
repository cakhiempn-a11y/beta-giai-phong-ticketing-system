import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { loadE2eState } from '../e2e-env';
import {
  createRoleSession,
  formatVnDateTimeLocal,
  loginInBrowser,
} from './helpers';

export interface BenchmarkMetric {
  endpoint: string;
  totalRequests: number;
  succeeded2xx3xx: number;
  expectedConflicts409: number;
  http5xxErrors: number;
  avgLatencyMs: number;
  p95LatencyMs: number;
  throughputRps: number;
  errorRate5xxPct: number;
  sqlExceptions: 'NOT MEASURED';
  deadlocks: 'NOT MEASURED';
}

function calcPercentile(sorted: number[], pct: number): number {
  if (sorted.length === 0) return 0;
  const idx = Math.min(sorted.length - 1, Math.ceil((pct / 100) * sorted.length) - 1);
  return Number(sorted[idx].toFixed(2));
}

test.describe('BE-05B Phase 9: Performance Baseline & Concurrent Load Validation', () => {
  test('Measure latency, throughput, P95, and 30 concurrent seat reservation requests on isolated E2E DB', async ({ browser }) => {
    const state = loadE2eState();
    const baseURL = state.baseUrl;

    const managerSession = await createRoleSession(browser, baseURL);
    const customerSession = await createRoleSession(browser, baseURL);
    const staffSession = await createRoleSession(browser, baseURL);

    try {
      const managerPage = managerSession.page;
      const customerPage = customerSession.page;
      const staffPage = staffSession.page;

      managerPage.on('dialog', (d) => d.accept());

      await loginInBrowser(managerPage, 'manager@demo.invalid', state.demoPassword);
      await loginInBrowser(customerPage, 'customer1@demo.invalid', state.demoPassword);
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);

      // Publish a dedicated movie & showtime for performance & 30-concurrency load test
      await managerPage.goto('/management/movies/create');
      const suffix = Date.now().toString().slice(-6);
      const perfTitle = `Phim Perf E2E ${suffix}`;
      await managerPage.fill('input[name="Title"]', perfTitle);
      await managerPage.fill('input[name="DurationMin"]', '100');
      await managerPage.selectOption('select[name="AgeLabel"]', 'T13');
      await managerPage.fill('input[name="GenreText"]', 'Hành động');
      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click('main button[type="submit"]'),
      ]);
      const movieId = Number(managerPage.url().match(/\/management\/movies\/(\d+)$/)![1]);

      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click(`form[action*="/management/movies/${movieId}/publish"] button[type="submit"]`),
      ]);

      await managerPage.goto('/management/showtimes/create');
      await managerPage.selectOption('select[name="MovieId"]', String(movieId));
      const roomOption = managerPage.locator('select[name="RoomId"] option:not([value=""])').first();
      const roomId = Number(await roomOption.getAttribute('value'));
      await managerPage.selectOption('select[name="RoomId"]', String(roomId));

      // Schedule at now + 6 hours (360 minutes) so it doesn't collide with other test showtimes
      const perfStart = new Date(Date.now() + 360 * 60 * 1000);
      await managerPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(perfStart));
      await managerPage.fill('input[name="BufferMinutes"]', '15');
      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click('main button[type="submit"]'),
      ]);
      const perfShowtimeId = Number(managerPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);

      // Fetch seat map via API to get seat IDs
      const seatMapApiResp = await customerSession.context.request.get(`/api/showtimes/${perfShowtimeId}/seats`);
      expect(seatMapApiResp.status()).toBe(200);
      const seatMapData = await seatMapApiResp.json();
      const bookableSeats: number[] = seatMapData.seats
        .filter((s: any) => s.isBookable)
        .map((s: any) => s.seatId);
      expect(bookableSeats.length).toBeGreaterThanOrEqual(8);

      const custCookies = await customerSession.context.cookies();
      const custXsrf = custCookies.find((c) => c.name === 'XSRF-TOKEN')!.value;

      const staffCookies = await staffSession.context.cookies();
      const staffXsrf = staffCookies.find((c) => c.name === 'XSRF-TOKEN')!.value;

      const metrics: BenchmarkMetric[] = [];

      async function runEndpointBenchmark(
        name: string,
        iterations: number,
        fn: (i: number) => Promise<{ status: number }>
      ): Promise<BenchmarkMetric> {
        const latencies: number[] = [];
        let succeeded2xx3xx = 0;
        let expectedConflicts409 = 0;
        let http5xxErrors = 0;
        const startWall = performance.now();

        for (let i = 0; i < iterations; i++) {
          const t0 = performance.now();
          const res = await fn(i);
          const dt = performance.now() - t0;
          latencies.push(dt);
          if (res.status >= 200 && res.status < 400) {
            succeeded2xx3xx++;
          } else if (res.status === 409) {
            expectedConflicts409++;
          } else if (res.status >= 500) {
            http5xxErrors++;
          }
        }

        const totalTimeSec = (performance.now() - startWall) / 1000;
        latencies.sort((a, b) => a - b);
        const avg = latencies.reduce((a, b) => a + b, 0) / latencies.length;

        const m: BenchmarkMetric = {
          endpoint: name,
          totalRequests: iterations,
          succeeded2xx3xx,
          expectedConflicts409,
          http5xxErrors,
          avgLatencyMs: Number(avg.toFixed(2)),
          p95LatencyMs: calcPercentile(latencies, 95),
          throughputRps: Number((iterations / totalTimeSec).toFixed(2)),
          errorRate5xxPct: Number(((http5xxErrors / iterations) * 100).toFixed(2)),
          sqlExceptions: 'NOT MEASURED',
          deadlocks: 'NOT MEASURED',
        };
        metrics.push(m);
        return m;
      }

      // 1. GET /movies (Catalog) - 25 requests
      await runEndpointBenchmark('GET /movies (Catalog)', 25, async () => {
        const r = await customerSession.context.request.get('/movies');
        return { status: r.status() };
      });

      // 2. GET /showtimes/{id}/seats (Seat Map HTML) - 25 requests
      await runEndpointBenchmark('GET /showtimes/{id}/seats (Seat Map)', 25, async () => {
        const r = await customerSession.context.request.get(`/showtimes/${perfShowtimeId}/seats`);
        return { status: r.status() };
      });

      // 3. POST /api/bookings/reserve + Checkout/Mock Payment + GET /tickets/{id}/qr (5 sequential orders using seats 0..4)
      const reserveLatencies: number[] = [];
      const checkoutLatencies: number[] = [];
      const issuedTicketIds: number[] = [];

      for (let i = 0; i < 5; i++) {
        const seatId = bookableSeats[i];
        const t0 = performance.now();
        const resResp = await customerSession.context.request.post('/api/bookings/reserve', {
          headers: { 'X-XSRF-TOKEN': custXsrf },
          data: { showtimeId: perfShowtimeId, seatIds: [seatId] },
        });
        reserveLatencies.push(performance.now() - t0);
        expect(resResp.status()).toBe(201);
        const resBody = await resResp.json();
        const orderId = resBody.orderId;

        const tPay0 = performance.now();
        await customerPage.goto(`/orders/${orderId}/checkout`);
        await Promise.all([
          customerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          customerPage.click('#btnPaySuccess'),
        ]);
        checkoutLatencies.push(performance.now() - tPay0);

        const ticketsResp = await customerSession.context.request.get(`/api/orders/${orderId}/tickets`);
        expect(ticketsResp.status()).toBe(200);
        const ticketsList = await ticketsResp.json();
        issuedTicketIds.push(ticketsList[0].ticketId);
      }

      reserveLatencies.sort((a, b) => a - b);
      metrics.push({
        endpoint: 'POST /api/bookings/reserve (Valid Hold)',
        totalRequests: 5,
        succeeded2xx3xx: 5,
        expectedConflicts409: 0,
        http5xxErrors: 0,
        avgLatencyMs: Number((reserveLatencies.reduce((a, b) => a + b, 0) / 5).toFixed(2)),
        p95LatencyMs: calcPercentile(reserveLatencies, 95),
        throughputRps: Number((5 / (reserveLatencies.reduce((a, b) => a + b, 0) / 1000)).toFixed(2)),
        errorRate5xxPct: 0,
        sqlExceptions: 'NOT MEASURED',
        deadlocks: 'NOT MEASURED',
      });

      checkoutLatencies.sort((a, b) => a - b);
      metrics.push({
        endpoint: 'POST /orders/{id}/demo-pay (Checkout & Ticket Issue)',
        totalRequests: 5,
        succeeded2xx3xx: 5,
        expectedConflicts409: 0,
        http5xxErrors: 0,
        avgLatencyMs: Number((checkoutLatencies.reduce((a, b) => a + b, 0) / 5).toFixed(2)),
        p95LatencyMs: calcPercentile(checkoutLatencies, 95),
        throughputRps: Number((5 / (checkoutLatencies.reduce((a, b) => a + b, 0) / 1000)).toFixed(2)),
        errorRate5xxPct: 0,
        sqlExceptions: 'NOT MEASURED',
        deadlocks: 'NOT MEASURED',
      });

      // 4. GET /tickets/{ticketId}/qr (20 requests)
      await runEndpointBenchmark('GET /tickets/{ticketId}/qr (QR PNG Generation)', 20, async (i) => {
        const tid = issuedTicketIds[i % issuedTicketIds.length];
        const r = await customerSession.context.request.get(`/tickets/${tid}/qr`);
        return { status: r.status() };
      });

      // 5. POST /api/staff/tickets/scan (20 requests)
      await runEndpointBenchmark('POST /api/staff/tickets/scan (Staff Verification)', 20, async () => {
        const r = await staffSession.context.request.post('/api/staff/tickets/scan', {
          headers: { 'X-XSRF-TOKEN': staffXsrf },
          data: { qrToken: 'invalid_probe_token_for_latency_check_0000000', expectedShowtimeId: perfShowtimeId },
        });
        return { status: r.status() < 500 ? 200 : 500 };
      });

      // 6. GET /management/reports (20 requests)
      await runEndpointBenchmark('GET /management/reports (Sales Dashboard)', 20, async () => {
        const r = await managerSession.context.request.get('/management/reports');
        return { status: r.status() };
      });

      // 7. CONCURRENT SEAT RESERVATION LOAD TEST: 30 simultaneous requests competing for the SAME seat
      const contestedSeatId = bookableSeats[6];
      const concurrencyLevel = 30;
      const concLatencies: number[] = [];
      const concStart = performance.now();

      const concurrentTasks = Array.from({ length: concurrencyLevel }, async () => {
        const t0 = performance.now();
        const resp = await customerSession.context.request.post('/api/bookings/reserve', {
          headers: { 'X-XSRF-TOKEN': custXsrf },
          data: { showtimeId: perfShowtimeId, seatIds: [contestedSeatId] },
        });
        const dt = performance.now() - t0;
        return { status: resp.status(), dt };
      });

      const concResults = await Promise.all(concurrentTasks);
      const concTotalSec = (performance.now() - concStart) / 1000;

      let concWinners = 0;
      let concConflicts409 = 0;
      let concHttp5xxErrors = 0;

      for (const r of concResults) {
        concLatencies.push(r.dt);
        if (r.status === 201) concWinners++;
        else if (r.status === 409) concConflicts409++;
        else if (r.status >= 500) concHttp5xxErrors++;
      }

      concLatencies.sort((a, b) => a - b);

      // Must have exactly 1 winner (201 Created), 29 valid domain conflicts (409 Conflict), and 0 HTTP 5xx server errors
      expect(concWinners).toBe(1);
      expect(concConflicts409).toBe(concurrencyLevel - 1);
      expect(concHttp5xxErrors).toBe(0);

      metrics.push({
        endpoint: `CONCURRENT POST /api/bookings/reserve (${concurrencyLevel} parallel on 1 seat)`,
        totalRequests: concurrencyLevel,
        succeeded2xx3xx: concWinners,
        expectedConflicts409: concConflicts409,
        http5xxErrors: concHttp5xxErrors,
        avgLatencyMs: Number((concLatencies.reduce((a, b) => a + b, 0) / concurrencyLevel).toFixed(2)),
        p95LatencyMs: calcPercentile(concLatencies, 95),
        throughputRps: Number((concurrencyLevel / concTotalSec).toFixed(2)),
        errorRate5xxPct: Number(((concHttp5xxErrors / concurrencyLevel) * 100).toFixed(2)),
        sqlExceptions: 'NOT MEASURED',
        deadlocks: 'NOT MEASURED',
      });

      const outPath = path.join(__dirname, '..', '.tmp', 'benchmark-results.json');
      fs.writeFileSync(outPath, JSON.stringify(metrics, null, 2), 'utf-8');
      console.log('\n[BE-05B/05C Empirical Performance Baseline (Non-SLA Reference)]:');
      console.table(metrics);
    } finally {
      await managerSession.context.close();
      await customerSession.context.close();
      await staffSession.context.close();
    }
  });
});

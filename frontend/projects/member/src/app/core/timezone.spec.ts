import { todayInZone, zonedTimeToUtc } from './timezone';

describe('zonedTimeToUtc', () => {
  it('maps London summer time (BST, UTC+1) correctly', () => {
    const instant = zonedTimeToUtc({ year: 2026, month: 6, day: 10 }, 9, 0, 'Europe/London');
    expect(instant.toISOString()).toBe('2026-06-10T08:00:00.000Z');
  });

  it('maps London winter time (GMT, UTC+0) correctly', () => {
    const instant = zonedTimeToUtc({ year: 2026, month: 1, day: 15 }, 9, 0, 'Europe/London');
    expect(instant.toISOString()).toBe('2026-01-15T09:00:00.000Z');
  });

  it('maps Berlin summer time (CEST, UTC+2) correctly', () => {
    const instant = zonedTimeToUtc({ year: 2026, month: 6, day: 10 }, 9, 30, 'Europe/Berlin');
    expect(instant.toISOString()).toBe('2026-06-10T07:30:00.000Z');
  });

  it('maps a zone east of the date line correctly', () => {
    const instant = zonedTimeToUtc({ year: 2026, month: 6, day: 10 }, 8, 0, 'Pacific/Auckland');
    expect(instant.toISOString()).toBe('2026-06-09T20:00:00.000Z');
  });
});

describe('todayInZone', () => {
  it('returns a plausible calendar date', () => {
    const today = todayInZone('Europe/London');
    expect(today.year).toBeGreaterThanOrEqual(2026);
    expect(today.month).toBeGreaterThanOrEqual(1);
    expect(today.month).toBeLessThanOrEqual(12);
    expect(today.day).toBeGreaterThanOrEqual(1);
    expect(today.day).toBeLessThanOrEqual(31);
  });
});

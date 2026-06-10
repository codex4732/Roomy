/**
 * Minimal IANA time zone helpers. Bookings are UTC instants; the availability grid
 * is laid out in the *location's* zone (FR-2.1), which may differ from the browser's.
 */

interface DateParts {
  year: number;
  month: number;
  day: number;
}

/** Calendar date of "now" in the given zone. */
export function todayInZone(timeZone: string): DateParts {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());
  const get = (type: string): number => Number(parts.find((p) => p.type === type)?.value);
  return { year: get('year'), month: get('month'), day: get('day') };
}

/** UTC instant for the given wall-clock time in the given zone (two-pass offset estimate). */
export function zonedTimeToUtc(
  { year, month, day }: DateParts,
  hour: number,
  minute: number,
  timeZone: string,
): Date {
  const utcGuess = Date.UTC(year, month - 1, day, hour, minute);
  const offset = offsetAt(new Date(utcGuess), timeZone);
  // Re-evaluate once in case the first guess straddled a DST transition.
  const better = utcGuess - offset;
  return new Date(utcGuess - offsetAt(new Date(better), timeZone));
}

/** Zone offset (ms east of UTC) at the given instant. */
function offsetAt(instant: Date, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).formatToParts(instant);
  const get = (type: string): number => Number(parts.find((p) => p.type === type)?.value);
  const asUtc = Date.UTC(
    get('year'), get('month') - 1, get('day'),
    get('hour') % 24, get('minute'), get('second'),
  );
  return asUtc - instant.getTime();
}

import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import {
  BookingDto,
  LocationDto,
  RoomDto,
  RoomsService,
} from '../../core/rooms.service';
import { todayInZone, zonedTimeToUtc } from '../../core/timezone';

interface TimeSlot {
  label: string;
  hour: number;
  minute: number;
}

interface BlackoutDto {
  id: string;
  locationId: string;
  roomId: string | null;
  reason: string;
  startAt: string;
  endAt: string;
}

interface MineDto {
  id: string;
  roomId: string;
  roomName: string;
  locationName: string;
  title: string;
  startAt: string;
  endAt: string;
  status: string;
  seriesId: string | null;
  checkinRequired: boolean;
  setupNotes: string | null;
  participants: string[];
}

type GridCell =
  | { kind: 'free'; slot: TimeSlot; span: 1 }
  | { kind: 'booking'; slot: TimeSlot; span: number; booking: BookingDto }
  | { kind: 'blackout'; slot: TimeSlot; span: number; reason: string };

interface BookingDraft {
  room: RoomDto;
  slot: TimeSlot;
  title: string;
  startTime: string;
  endTime: string;
  endDate: string;
  repeat: 'none' | 'daily' | 'weekly';
  count: number;
  conflictCount: number;
  advanced: boolean;
  participantsText: string;
  setupNotes: string;
}

const DAY_START_HOUR = 8;
const DAY_END_HOUR = 18;
const SLOT_MINUTES = 30;

const TILE_GRADIENTS = [
  'linear-gradient(135deg, #818cf8, #6366f1)',
  'linear-gradient(135deg, #34d399, #10b981)',
  'linear-gradient(135deg, #fbbf24, #f59e0b)',
  'linear-gradient(135deg, #f472b6, #ec4899)',
  'linear-gradient(135deg, #38bdf8, #0ea5e9)',
  'linear-gradient(135deg, #a78bfa, #8b5cf6)',
];

@Component({
  selector: 'app-rooms',
  imports: [FormsModule],
  templateUrl: './rooms.html',
  styleUrl: './rooms.scss',
})
export class Rooms {
  protected readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly roomsService = inject(RoomsService);

  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly selectedLocationId = signal<string | null>(null);
  protected readonly rooms = signal<RoomDto[]>([]);
  protected readonly bookings = signal<BookingDto[]>([]);
  protected readonly blackouts = signal<BlackoutDto[]>([]);
  protected readonly mine = signal<MineDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly draft = signal<BookingDraft | null>(null);
  protected readonly selected = signal<BookingDto | null>(null);
  protected readonly saving = signal(false);
  protected readonly graceMinutes = signal(10);
  protected readonly view = signal<'grid' | 'mine'>('grid');
  protected readonly gridMode = signal<'day' | 'week'>('day');
  protected readonly selectedDate = signal<string>('');
  protected readonly search = signal('');
  protected readonly minCapacity = signal(0);
  private readonly tick = signal(Date.now());

  protected readonly selectedLocation = computed(() =>
    this.locations().find((l) => l.id === this.selectedLocationId()) ?? null,
  );

  protected readonly slots: TimeSlot[] = buildSlots();
  protected readonly timeOptions = buildTimeOptions();
  protected readonly capacityOptions = [0, 2, 4, 8, 12];

  protected readonly greeting = computed(() => {
    const hour = new Date(this.tick()).getHours();
    const name = this.auth.user()?.name.split(' ')[0] ?? '';
    const part = hour < 12 ? 'morning' : hour < 18 ? 'afternoon' : 'evening';
    return `Good ${part}, ${name}`;
  });

  protected readonly todayString = computed(() => {
    const tz = this.selectedLocation()?.timezone;
    return tz ? dateString(todayInZone(tz)) : '';
  });

  protected readonly selectedDateLabel = computed(() => {
    const date = this.selectedDate();
    if (!date) {
      return '';
    }
    return new Date(`${date}T12:00:00`).toLocaleDateString(undefined, {
      weekday: 'long', month: 'long', day: 'numeric',
    });
  });

  /** Monday-to-Sunday date strings of the week containing the selected date. */
  protected readonly weekDates = computed(() => {
    const date = this.selectedDate();
    if (!date) {
      return [];
    }
    const base = new Date(`${date}T12:00:00Z`);
    const monday = new Date(base);
    monday.setUTCDate(base.getUTCDate() - ((base.getUTCDay() + 6) % 7));
    return Array.from({ length: 7 }, (_, i) => {
      const d = new Date(monday);
      d.setUTCDate(monday.getUTCDate() + i);
      return d.toISOString().slice(0, 10);
    });
  });

  protected readonly filteredRooms = computed(() => {
    const query = this.search().trim().toLowerCase();
    const min = this.minCapacity();
    return this.rooms().filter((r) =>
      r.capacity >= min && (!query || r.name.toLowerCase().includes(query)));
  });

  protected readonly slotMap = computed(() => {
    const location = this.selectedLocation();
    const date = this.selectedDate();
    const map = new Map<string, BookingDto>();
    if (!location || !date) {
      return map;
    }
    for (const booking of this.bookings()) {
      const start = new Date(booking.startAt).getTime();
      const end = new Date(booking.endAt).getTime();
      for (const slot of this.slots) {
        const slotStart = this.slotInstant(slot, date, location).getTime();
        if (start < slotStart + SLOT_MINUTES * 60_000 && end > slotStart) {
          map.set(`${booking.roomId}|${slot.label}`, booking);
        }
      }
    }
    return map;
  });

  protected readonly freeNowCount = computed(() => {
    const now = this.tick();
    const busyRooms = new Set(this.bookings()
      .filter((b) => new Date(b.startAt).getTime() <= now && new Date(b.endAt).getTime() > now)
      .map((b) => b.roomId));
    return this.rooms().filter((r) => !busyRooms.has(r.id)).length;
  });

  protected readonly myUpcoming = computed(() =>
    this.mine().filter((b) => !['Cancelled', 'Declined'].includes(b.status)));

  protected readonly myPendingCount = computed(() =>
    this.myUpcoming().filter((b) => b.status === 'PendingApproval').length);

  protected readonly nowSlotLabel = computed(() => {
    const location = this.selectedLocation();
    if (!location || this.selectedDate() !== this.todayString()) {
      return null;
    }
    const now = this.tick();
    for (const slot of this.slots) {
      const start = this.slotInstant(slot, this.selectedDate(), location).getTime();
      if (now >= start && now < start + SLOT_MINUTES * 60_000) {
        return slot.label;
      }
    }
    return null;
  });

  /** roomId|date -> booking count, for the week overview. */
  protected readonly weekCounts = computed(() => {
    const tz = this.selectedLocation()?.timezone;
    const map = new Map<string, number>();
    if (!tz) {
      return map;
    }
    for (const booking of this.bookings()) {
      const day = dateInZone(booking.startAt, tz);
      const key = `${booking.roomId}|${day}`;
      map.set(key, (map.get(key) ?? 0) + 1);
    }
    return map;
  });

  constructor() {
    void this.loadLocations();
    void this.loadSettings();
    void this.loadMine();
    setInterval(() => this.tick.set(Date.now()), 30_000);
  }

  protected weekCount(roomId: string, date: string): number {
    return this.weekCounts().get(`${roomId}|${date}`) ?? 0;
  }

  protected dayLabel(date: string): string {
    return new Date(`${date}T12:00:00`).toLocaleDateString(undefined, {
      weekday: 'short', day: 'numeric',
    });
  }

  protected bookingAt(roomId: string, slot: TimeSlot): BookingDto | undefined {
    return this.slotMap().get(`${roomId}|${slot.label}`);
  }

  protected blackoutAt(roomId: string, slot: TimeSlot): BlackoutDto | undefined {
    const location = this.selectedLocation();
    const date = this.selectedDate();
    if (!location || !date) {
      return undefined;
    }
    const slotStart = this.slotInstant(slot, date, location).getTime();
    const slotEnd = slotStart + SLOT_MINUTES * 60_000;
    return this.blackouts().find((b) =>
      (b.roomId === roomId || b.roomId === null)
      && new Date(b.startAt).getTime() < slotEnd
      && new Date(b.endAt).getTime() > slotStart);
  }

  protected cellsFor(room: RoomDto): GridCell[] {
    const cells: GridCell[] = [];
    let i = 0;
    while (i < this.slots.length) {
      const slot = this.slots[i];
      const booking = this.bookingAt(room.id, slot);
      if (booking) {
        let span = 1;
        while (i + span < this.slots.length
          && this.bookingAt(room.id, this.slots[i + span])?.id === booking.id) {
          span++;
        }
        cells.push({ kind: 'booking', slot, span, booking });
        i += span;
        continue;
      }
      const blackout = this.blackoutAt(room.id, slot);
      if (blackout) {
        let span = 1;
        while (i + span < this.slots.length
          && !this.bookingAt(room.id, this.slots[i + span])
          && this.blackoutAt(room.id, this.slots[i + span])?.id === blackout.id) {
          span++;
        }
        cells.push({ kind: 'blackout', slot, span, reason: blackout.reason });
        i += span;
        continue;
      }
      cells.push({ kind: 'free', slot, span: 1 });
      i++;
    }
    return cells;
  }

  protected tile(room: { name: string }): string {
    let hash = 0;
    for (const ch of room.name) {
      hash = (hash * 31 + ch.charCodeAt(0)) | 0;
    }
    return TILE_GRADIENTS[Math.abs(hash) % TILE_GRADIENTS.length];
  }

  protected roomName(roomId: string): string {
    return this.rooms().find((r) => r.id === roomId)?.name ?? 'Room';
  }

  protected canCheckIn(booking: { isMine?: boolean; status: string; checkinRequired: boolean; startAt: string }): boolean {
    if (booking.isMine === false || booking.status !== 'Confirmed' || !booking.checkinRequired) {
      return false;
    }
    const now = this.tick();
    const start = new Date(booking.startAt).getTime();
    return now >= start - 10 * 60_000 && now <= start + this.graceMinutes() * 60_000;
  }

  protected fmt(iso: string): string {
    const tz = this.selectedLocation()?.timezone;
    return new Date(iso).toLocaleTimeString(undefined, {
      hour: '2-digit', minute: '2-digit', timeZone: tz,
    });
  }

  protected fmtDate(iso: string): string {
    return new Date(iso).toLocaleDateString(undefined, {
      weekday: 'short', month: 'short', day: 'numeric',
    });
  }

  protected statusLabel(status: string): string {
    return status === 'PendingApproval' ? 'Awaiting approval'
      : status === 'CheckedIn' ? 'In progress'
      : status === 'AutoReleased' ? 'No-show'
      : status;
  }

  protected async selectLocation(id: string): Promise<void> {
    this.selectedLocationId.set(id);
    this.closePanels();
    this.loading.set(true);
    this.error.set(null);
    const location = this.locations().find((l) => l.id === id);
    if (location && !this.selectedDate()) {
      this.selectedDate.set(dateString(todayInZone(location.timezone)));
    }
    try {
      const [rooms] = await Promise.all([this.roomsService.listRooms(id), this.refresh(id)]);
      this.rooms.set(rooms);
    } catch {
      this.rooms.set([]);
      this.error.set('Could not load rooms for this location.');
    } finally {
      this.loading.set(false);
    }
  }

  protected onLocationChange(event: Event): void {
    void this.selectLocation((event.target as HTMLSelectElement).value);
  }

  protected async setDate(date: string): Promise<void> {
    if (!date) {
      return;
    }
    this.selectedDate.set(date);
    this.closePanels();
    const id = this.selectedLocationId();
    if (id) {
      await this.refresh(id);
    }
  }

  protected shiftDate(days: number): void {
    const current = new Date(`${this.selectedDate()}T12:00:00Z`);
    current.setUTCDate(current.getUTCDate() + days);
    void this.setDate(current.toISOString().slice(0, 10));
  }

  protected openDay(date: string): void {
    this.gridMode.set('day');
    void this.setDate(date);
  }

  protected openBooking(booking: BookingDto): void {
    if (booking.isMine) {
      this.draft.set(null);
      this.selected.set(booking);
    }
  }

  protected openDraft(room: RoomDto, slot: TimeSlot): void {
    const location = this.selectedLocation();
    if (!location) {
      return;
    }
    this.notice.set(null);
    this.selected.set(null);
    const endIndex = this.timeOptions.indexOf(slot.label) + 2;
    this.draft.set({
      room,
      slot,
      title: '',
      startTime: slot.label,
      endTime: this.timeOptions[Math.min(endIndex, this.timeOptions.length - 1)],
      endDate: this.selectedDate(),
      repeat: 'none',
      count: 4,
      conflictCount: 0,
      advanced: false,
      participantsText: '',
      setupNotes: '',
    });
  }

  protected closePanels(): void {
    this.draft.set(null);
    this.selected.set(null);
  }

  protected async submitDraft(skipConflicts = false): Promise<void> {
    const draft = this.draft();
    const location = this.selectedLocation();
    if (!draft || !location || !draft.title.trim()) {
      return;
    }
    const tz = location.timezone;
    const [sh, sm] = draft.startTime.split(':').map(Number);
    const [eh, em] = draft.endTime.split(':').map(Number);
    const [sy, smo, sd] = this.selectedDate().split('-').map(Number);
    const [ey, emo, ed] = draft.endDate.split('-').map(Number);
    const start = zonedTimeToUtc({ year: sy, month: smo, day: sd }, sh, sm, tz);
    const end = zonedTimeToUtc({ year: ey, month: emo, day: ed }, eh, em, tz);
    if (end <= start) {
      this.notice.set('End must be after start.');
      return;
    }
    const participants = draft.participantsText
      .split(',').map((p) => p.trim()).filter((p) => p.length > 0);
    const setupNotes = draft.setupNotes.trim() || null;

    this.saving.set(true);
    try {
      if (draft.repeat === 'none') {
        const created = await this.roomsService.createBooking({
          roomId: draft.room.id,
          title: draft.title.trim(),
          startAt: start.toISOString(),
          endAt: end.toISOString(),
          participants,
          setupNotes,
        });
        this.notice.set(created.status === 'PendingApproval'
          ? `Requested ${draft.room.name} — awaiting approval.`
          : `Booked ${draft.room.name} at ${draft.startTime}.`);
      } else {
        const result = await firstValueFrom(this.http.post<{ created: BookingDto[]; skipped: string[] }>(
          '/api/v1/bookings/series',
          {
            roomId: draft.room.id,
            title: draft.title.trim(),
            startAt: start.toISOString(),
            endAt: end.toISOString(),
            frequency: draft.repeat,
            count: draft.count,
            skipConflicts,
            participants,
            setupNotes,
          },
        ));
        this.notice.set(`Booked ${result.created.length} occurrence(s)` +
          (result.skipped.length ? `, skipped ${result.skipped.length} conflict(s).` : '.'));
      }
      this.draft.set(null);
      await Promise.all([this.refresh(location.id), this.loadMine()]);
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        if (draft.repeat !== 'none' && error.error?.conflictDates) {
          this.draft.set({ ...draft, conflictCount: error.error.conflictDates.length });
        } else {
          this.notice.set('That slot was just taken — pick another time or room.');
          this.draft.set(null);
          await this.refresh(location.id);
        }
      } else if (error instanceof HttpErrorResponse && error.status === 422) {
        this.notice.set(error.error?.detail ?? 'The booking was rejected.');
      } else {
        this.notice.set('Booking failed — try again.');
      }
    } finally {
      this.saving.set(false);
    }
  }

  protected async act(
    booking: { id: string; seriesId: string | null },
    action: 'check-in' | 'end' | 'cancel' | 'cancel-series',
  ): Promise<void> {
    this.saving.set(true);
    try {
      if (action === 'cancel-series') {
        await firstValueFrom(this.http.post(`/api/v1/bookings/series/${booking.seriesId}/cancel`, {}));
        this.notice.set('Series cancelled.');
      } else {
        await firstValueFrom(this.http.post(`/api/v1/bookings/${booking.id}/${action}`, {}));
        this.notice.set(action === 'check-in' ? 'Checked in — enjoy your meeting!'
          : action === 'end' ? 'Meeting ended, room released.'
          : 'Booking cancelled.');
      }
      this.selected.set(null);
      const id = this.selectedLocationId();
      await Promise.all([id ? this.refresh(id) : Promise.resolve(), this.loadMine()]);
    } catch (error: unknown) {
      this.notice.set(error instanceof HttpErrorResponse && error.status === 422
        ? (error.error?.detail ?? 'Not allowed.')
        : 'Action failed — try again.');
    } finally {
      this.saving.set(false);
    }
  }

  protected logout(): void {
    void this.auth.logout();
  }

  private async loadLocations(): Promise<void> {
    try {
      const locations = await this.roomsService.listLocations();
      this.locations.set(locations);
      if (locations.length > 0) {
        await this.selectLocation(locations[0].id);
      }
    } catch {
      this.error.set('Could not load locations.');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    try {
      const settings = await firstValueFrom(
        this.http.get<{ checkinGraceMinutes: number }>('/api/v1/settings'),
      );
      this.graceMinutes.set(settings.checkinGraceMinutes);
    } catch {
      // Defaults are fine if settings are unavailable.
    }
  }

  private async loadMine(): Promise<void> {
    try {
      this.mine.set(await firstValueFrom(this.http.get<MineDto[]>('/api/v1/bookings/mine')));
    } catch {
      // Sidebar badge simply stays stale.
    }
  }

  /** Fetches bookings and blackouts for the whole week around the selected date. */
  private async refresh(locationId: string): Promise<void> {
    const location = this.locations().find((l) => l.id === locationId);
    const week = this.weekDates();
    if (!location || week.length === 0) {
      return;
    }
    const tz = location.timezone;
    const from = zonedTimeToUtc(parseDate(week[0]), 0, 0, tz).toISOString();
    const to = zonedTimeToUtc(parseDate(week[6]), 23, 59, tz).toISOString();
    const [bookings, blackouts] = await Promise.all([
      this.roomsService.listBookings(locationId, from, to),
      firstValueFrom(this.http.get<BlackoutDto[]>('/api/v1/blackouts',
        { params: { locationId, from, to } })),
    ]);
    this.bookings.set(bookings);
    this.blackouts.set(blackouts);
  }

  private slotInstant(slot: TimeSlot, date: string, location: LocationDto): Date {
    return zonedTimeToUtc(parseDate(date), slot.hour, slot.minute, location.timezone);
  }
}

function parseDate(date: string): { year: number; month: number; day: number } {
  const [year, month, day] = date.split('-').map(Number);
  return { year, month, day };
}

function dateString(d: { year: number; month: number; day: number }): string {
  return `${d.year}-${String(d.month).padStart(2, '0')}-${String(d.day).padStart(2, '0')}`;
}

function dateInZone(iso: string, timeZone: string): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(new Date(iso));
}

function buildTimeOptions(): string[] {
  const options: string[] = [];
  for (let hour = 0; hour < 24; hour++) {
    for (const minute of [0, 15, 30, 45]) {
      options.push(`${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`);
    }
  }
  return options;
}

function buildSlots(): TimeSlot[] {
  const slots: TimeSlot[] = [];
  for (let hour = DAY_START_HOUR; hour < DAY_END_HOUR; hour++) {
    for (const minute of [0, SLOT_MINUTES]) {
      slots.push({
        hour, minute,
        label: `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`,
      });
    }
  }
  return slots;
}

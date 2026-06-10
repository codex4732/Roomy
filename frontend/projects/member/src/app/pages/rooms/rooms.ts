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

interface BookingDraft {
  room: RoomDto;
  slot: TimeSlot;
  startIso: string;
  title: string;
  durationMinutes: number;
  repeat: 'none' | 'daily' | 'weekly';
  count: number;
  conflictCount: number;
}

const DAY_START_HOUR = 8;
const DAY_END_HOUR = 18;
const SLOT_MINUTES = 30;

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
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly draft = signal<BookingDraft | null>(null);
  protected readonly selected = signal<BookingDto | null>(null);
  protected readonly saving = signal(false);
  protected readonly graceMinutes = signal(10);
  private readonly tick = signal(Date.now());

  protected readonly selectedLocation = computed(() =>
    this.locations().find((l) => l.id === this.selectedLocationId()) ?? null,
  );

  protected readonly slots: TimeSlot[] = buildSlots();
  protected readonly durations = [30, 60, 90, 120];

  protected readonly today = new Date().toLocaleDateString(undefined, {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
  });

  protected readonly slotMap = computed(() => {
    const location = this.selectedLocation();
    const map = new Map<string, BookingDto>();
    if (!location) {
      return map;
    }
    for (const booking of this.bookings()) {
      const start = new Date(booking.startAt).getTime();
      const end = new Date(booking.endAt).getTime();
      for (const slot of this.slots) {
        const slotStart = this.slotInstant(slot, location).getTime();
        if (start < slotStart + SLOT_MINUTES * 60_000 && end > slotStart) {
          map.set(`${booking.roomId}|${slot.label}`, booking);
        }
      }
    }
    return map;
  });

  constructor() {
    void this.loadLocations();
    void this.loadSettings();
    setInterval(() => this.tick.set(Date.now()), 30_000);
  }

  protected bookingAt(roomId: string, slot: TimeSlot): BookingDto | undefined {
    return this.slotMap().get(`${roomId}|${slot.label}`);
  }

  protected canCheckIn(booking: BookingDto): boolean {
    if (!booking.isMine || booking.status !== 'Confirmed' || !booking.checkinRequired) {
      return false;
    }
    const now = this.tick();
    const start = new Date(booking.startAt).getTime();
    return now >= start - 10 * 60_000 && now <= start + this.graceMinutes() * 60_000;
  }

  protected fmt(iso: string): string {
    const tz = this.selectedLocation()?.timezone;
    return new Date(iso).toLocaleTimeString(undefined, {
      hour: '2-digit',
      minute: '2-digit',
      timeZone: tz,
    });
  }

  protected async selectLocation(id: string): Promise<void> {
    this.selectedLocationId.set(id);
    this.draft.set(null);
    this.selected.set(null);
    this.loading.set(true);
    this.error.set(null);
    try {
      const [rooms] = await Promise.all([this.roomsService.listRooms(id), this.refreshBookings(id)]);
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

  protected onSlotClick(room: RoomDto, slot: TimeSlot): void {
    const booking = this.bookingAt(room.id, slot);
    if (booking) {
      this.selected.set(booking.isMine ? booking : null);
      this.draft.set(null);
      return;
    }
    const location = this.selectedLocation();
    if (!location) {
      return;
    }
    this.notice.set(null);
    this.selected.set(null);
    this.draft.set({
      room,
      slot,
      startIso: this.slotInstant(slot, location).toISOString(),
      title: '',
      durationMinutes: 60,
      repeat: 'none',
      count: 4,
      conflictCount: 0,
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
    const start = new Date(draft.startIso);
    const end = new Date(start.getTime() + draft.durationMinutes * 60_000);

    this.saving.set(true);
    try {
      if (draft.repeat === 'none') {
        const created = await this.roomsService.createBooking({
          roomId: draft.room.id,
          title: draft.title.trim(),
          startAt: start.toISOString(),
          endAt: end.toISOString(),
        });
        this.notice.set(created.status === 'PendingApproval'
          ? `Requested ${draft.room.name} — awaiting approval.`
          : `Booked ${draft.room.name} at ${draft.slot.label}.`);
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
          },
        ));
        this.notice.set(`Booked ${result.created.length} occurrence(s)` +
          (result.skipped.length ? `, skipped ${result.skipped.length} conflict(s).` : '.'));
      }
      this.draft.set(null);
      await this.refreshBookings(location.id);
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        if (draft.repeat !== 'none' && error.error?.conflictDates) {
          this.draft.set({ ...draft, conflictCount: error.error.conflictDates.length });
        } else {
          this.notice.set('That slot was just taken — pick another time or room.');
          this.draft.set(null);
          await this.refreshBookings(location.id);
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

  protected async act(action: 'check-in' | 'end' | 'cancel' | 'cancel-series'): Promise<void> {
    const booking = this.selected();
    const location = this.selectedLocation();
    if (!booking || !location) {
      return;
    }
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
      await this.refreshBookings(location.id);
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

  private async refreshBookings(locationId: string): Promise<void> {
    const location = this.locations().find((l) => l.id === locationId);
    if (!location) {
      return;
    }
    const day = todayInZone(location.timezone);
    const from = zonedTimeToUtc(day, 0, 0, location.timezone).toISOString();
    const to = zonedTimeToUtc(day, 23, 59, location.timezone).toISOString();
    this.bookings.set(await this.roomsService.listBookings(locationId, from, to));
  }

  private slotInstant(slot: TimeSlot, location: LocationDto): Date {
    return zonedTimeToUtc(todayInZone(location.timezone), slot.hour, slot.minute, location.timezone);
  }
}

function buildSlots(): TimeSlot[] {
  const slots: TimeSlot[] = [];
  for (let hour = DAY_START_HOUR; hour < DAY_END_HOUR; hour++) {
    for (const minute of [0, SLOT_MINUTES]) {
      slots.push({
        hour,
        minute,
        label: `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`,
      });
    }
  }
  return slots;
}

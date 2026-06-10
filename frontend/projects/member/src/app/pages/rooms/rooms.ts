import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import {
  BookingDto,
  CreateBookingRequest,
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
  private readonly roomsService = inject(RoomsService);

  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly selectedLocationId = signal<string | null>(null);
  protected readonly rooms = signal<RoomDto[]>([]);
  protected readonly bookings = signal<BookingDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly draft = signal<BookingDraft | null>(null);
  protected readonly saving = signal(false);

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

  /** booking keyed by `${roomId}|${slot.label}` for O(1) grid lookups. */
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
  }

  protected bookingAt(roomId: string, slot: TimeSlot): BookingDto | undefined {
    return this.slotMap().get(`${roomId}|${slot.label}`);
  }

  protected async selectLocation(id: string): Promise<void> {
    this.selectedLocationId.set(id);
    this.draft.set(null);
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

  protected openDraft(room: RoomDto, slot: TimeSlot): void {
    const location = this.selectedLocation();
    if (!location || this.bookingAt(room.id, slot)) {
      return;
    }
    this.notice.set(null);
    this.draft.set({
      room,
      slot,
      startIso: this.slotInstant(slot, location).toISOString(),
      title: '',
      durationMinutes: 60,
    });
  }

  protected closeDraft(): void {
    this.draft.set(null);
  }

  protected async submitDraft(): Promise<void> {
    const draft = this.draft();
    const location = this.selectedLocation();
    if (!draft || !location || !draft.title.trim()) {
      return;
    }

    const start = new Date(draft.startIso);
    const end = new Date(start.getTime() + draft.durationMinutes * 60_000);
    const request: CreateBookingRequest = {
      roomId: draft.room.id,
      title: draft.title.trim(),
      startAt: start.toISOString(),
      endAt: end.toISOString(),
    };

    this.saving.set(true);
    try {
      await this.roomsService.createBooking(request);
      this.notice.set(`Booked ${draft.room.name} at ${draft.slot.label}.`);
      this.draft.set(null);
      await this.refreshBookings(location.id);
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        this.notice.set('That slot was just taken — pick another time or room.');
        await this.refreshBookings(location.id);
      } else if (error instanceof HttpErrorResponse && error.status === 422) {
        this.notice.set(error.error?.detail ?? 'The booking was rejected.');
      } else {
        this.notice.set('Booking failed — try again.');
      }
    } finally {
      this.saving.set(false);
    }
  }

  protected async cancelBooking(booking: BookingDto): Promise<void> {
    const location = this.selectedLocation();
    if (!location || !booking.isMine) {
      return;
    }
    if (!confirm(`Cancel "${booking.title}"?`)) {
      return;
    }
    try {
      await this.roomsService.cancelBooking(booking.id);
      this.notice.set(`Cancelled "${booking.title}".`);
      await this.refreshBookings(location.id);
    } catch {
      this.notice.set('Could not cancel that booking.');
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

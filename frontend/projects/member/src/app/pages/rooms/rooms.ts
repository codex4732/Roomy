import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth.service';
import { LocationDto, RoomDto, RoomsService } from '../../core/rooms.service';

interface TimeSlot {
  label: string;
  hour: number;
  minute: number;
}

const DAY_START_HOUR = 8;
const DAY_END_HOUR = 18;

@Component({
  selector: 'app-rooms',
  imports: [],
  templateUrl: './rooms.html',
  styleUrl: './rooms.scss',
})
export class Rooms {
  protected readonly auth = inject(AuthService);
  private readonly roomsService = inject(RoomsService);

  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly selectedLocationId = signal<string | null>(null);
  protected readonly rooms = signal<RoomDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly selectedLocation = computed(() =>
    this.locations().find((l) => l.id === this.selectedLocationId()) ?? null,
  );

  protected readonly slots: TimeSlot[] = buildSlots();
  protected readonly today = new Date().toLocaleDateString(undefined, {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
  });

  constructor() {
    void this.loadLocations();
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

  protected async selectLocation(id: string): Promise<void> {
    this.selectedLocationId.set(id);
    this.loading.set(true);
    this.error.set(null);
    try {
      this.rooms.set(await this.roomsService.listRooms(id));
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

  protected logout(): void {
    void this.auth.logout();
  }
}

function buildSlots(): TimeSlot[] {
  const slots: TimeSlot[] = [];
  for (let hour = DAY_START_HOUR; hour < DAY_END_HOUR; hour++) {
    for (const minute of [0, 30]) {
      slots.push({
        hour,
        minute,
        label: `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`,
      });
    }
  }
  return slots;
}

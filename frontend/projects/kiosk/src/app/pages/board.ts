import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../core/auth.service';

interface LocationDto { id: string; name: string; timezone: string; }
interface RoomDto { id: string; name: string; capacity: number; }
interface BookingDto {
  id: string; roomId: string; title: string; startAt: string; endAt: string;
  status: string; organizerName: string;
}

type BoardState = 'free' | 'busy' | 'soon';

@Component({
  selector: 'app-board',
  imports: [],
  template: `
    @if (!room()) {
      <main class="pick">
        <h1>Choose this kiosk's room</h1>
        @for (location of locations(); track location.id) {
          <h2>{{ location.name }}</h2>
          <div class="rooms">
            @for (r of roomsOf(location.id); track r.id) {
              <button (click)="choose(location, r)">{{ r.name }} · {{ r.capacity }}</button>
            }
          </div>
        }
        <button class="signout" (click)="auth.logout()">Sign out</button>
      </main>
    } @else {
      <main class="board" [class]="state()">
        <header>
          <span class="room-name">{{ room()!.name }}</span>
          <span class="when"><span class="date">{{ dateLabel() }}</span><span class="clock">{{ clock() }}</span></span>
        </header>
        <section class="status">
          @if (state() === 'free') {
            <h1>Free</h1>
            @if (next(); as n) {
              <p>Next: {{ n.title }} at {{ fmt(n.startAt) }}</p>
            } @else {
              <p>No more bookings today</p>
            }
          } @else if (current(); as c) {
            <h1>{{ c.title }}</h1>
            <p>{{ c.organizerName }} · until {{ fmt(c.endAt) }}</p>
            @if (c.status === 'Confirmed') {
              <p class="hint">Waiting for check-in</p>
            }
          }
        </section>
        <footer>
          <span>Upcoming:</span>
          @for (b of upcoming(); track b.id) {
            <span class="pill">{{ fmt(b.startAt) }} {{ b.title }}</span>
          } @empty {
            <span class="pill muted">nothing else today</span>
          }
          <button class="switch" (click)="room.set(null)">⚙</button>
        </footer>
      </main>
    }
  `,
  styles: `
    .pick {
      max-width: 700px; margin: 0 auto; padding: 2rem;
      h1 { color: #1d2c3c; }
      h2 { color: #44546a; margin-bottom: 0.5rem; }
      .rooms { display: flex; gap: 0.6rem; flex-wrap: wrap; margin-bottom: 1rem; }
      button { padding: 1rem 1.4rem; font-size: 1.1rem; border: none; border-radius: 12px; background: #15806b; color: #fff; cursor: pointer; }
      .signout { background: #eef1f5; color: #36465a; margin-top: 1.5rem; }
    }

    .board {
      min-height: 100vh; display: flex; flex-direction: column; color: #fff;
      transition: background 0.5s;
      &.free { background: linear-gradient(135deg, #0e7c52, #14a06b); }
      &.busy { background: linear-gradient(135deg, #9c2a1a, #c2402a); }
      &.soon { background: linear-gradient(135deg, #9a7300, #c79b15); }

      header {
        display: flex; justify-content: space-between; align-items: center; padding: 1.5rem 2rem;
        font-size: 1.6rem; font-weight: 700; opacity: 0.95;

        .when { display: flex; flex-direction: column; align-items: flex-end; }
        .date { font-size: 1rem; font-weight: 600; opacity: 0.85; }
      }

      .status {
        flex: 1; display: grid; place-content: center; text-align: center; padding: 0 2rem;
        h1 { font-size: clamp(3rem, 9vw, 6rem); margin: 0; }
        p { font-size: 1.5rem; margin: 0.6rem 0 0; opacity: 0.9; }
        .hint { font-size: 1.1rem; opacity: 0.75; }
      }

      footer {
        display: flex; gap: 0.6rem; align-items: center; padding: 1.2rem 2rem; flex-wrap: wrap;
        font-size: 1rem; opacity: 0.95;
        .pill { background: rgba(255, 255, 255, 0.18); border-radius: 999px; padding: 0.3rem 0.9rem; }
        .muted { opacity: 0.7; }
        .switch { margin-left: auto; background: rgba(255, 255, 255, 0.15); border: none; color: #fff; border-radius: 8px; padding: 0.4rem 0.7rem; cursor: pointer; }
      }
    }
  `,
})
export class Board {
  protected readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);

  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly roomsByLocation = signal<Map<string, RoomDto[]>>(new Map());
  protected readonly room = signal<RoomDto | null>(null);
  protected readonly location = signal<LocationDto | null>(null);
  protected readonly bookings = signal<BookingDto[]>([]);
  protected readonly clock = signal('');
  protected readonly dateLabel = signal('');
  private readonly now = signal(Date.now());

  protected readonly current = computed(() =>
    this.bookings().find((b) => new Date(b.startAt).getTime() <= this.now()
      && new Date(b.endAt).getTime() > this.now()) ?? null);

  protected readonly next = computed(() =>
    this.bookings().find((b) => new Date(b.startAt).getTime() > this.now()) ?? null);

  protected readonly upcoming = computed(() =>
    this.bookings().filter((b) => new Date(b.startAt).getTime() > this.now()).slice(0, 4));

  protected readonly state = computed<BoardState>(() => {
    if (this.current()) {
      return 'busy';
    }
    const next = this.next();
    return next && new Date(next.startAt).getTime() - this.now() < 15 * 60_000 ? 'soon' : 'free';
  });

  constructor() {
    void this.loadRooms();
    setInterval(() => {
      this.now.set(Date.now());
      this.updateClock();
      void this.refresh();
    }, 15_000);
    this.updateClock();
  }

  private updateClock(): void {
    const now = new Date();
    this.clock.set(now.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' }));
    this.dateLabel.set(now.toLocaleDateString(undefined, {
      weekday: 'long', day: 'numeric', month: 'long',
    }));
  }

  protected roomsOf(locationId: string): RoomDto[] {
    return this.roomsByLocation().get(locationId) ?? [];
  }

  protected fmt(iso: string): string {
    return new Date(iso).toLocaleTimeString(undefined, {
      hour: '2-digit', minute: '2-digit', timeZone: this.location()?.timezone,
    });
  }

  protected choose(location: LocationDto, room: RoomDto): void {
    this.location.set(location);
    this.room.set(room);
    void this.refresh();
  }

  private async loadRooms(): Promise<void> {
    const locations = await firstValueFrom(this.http.get<LocationDto[]>('/api/v1/locations'));
    this.locations.set(locations);
    const map = new Map<string, RoomDto[]>();
    await Promise.all(locations.map(async (l) => {
      map.set(l.id, await firstValueFrom(this.http.get<RoomDto[]>(`/api/v1/locations/${l.id}/rooms`)));
    }));
    this.roomsByLocation.set(map);
  }

  private async refresh(): Promise<void> {
    const location = this.location();
    const room = this.room();
    if (!location || !room) {
      return;
    }
    const from = new Date(Date.now() - 12 * 3600_000).toISOString();
    const to = new Date(Date.now() + 24 * 3600_000).toISOString();
    const all = await firstValueFrom(this.http.get<BookingDto[]>(
      '/api/v1/bookings', { params: { locationId: location.id, from, to } }));
    this.bookings.set(all
      .filter((b) => b.roomId === room.id)
      .sort((a, b) => a.startAt.localeCompare(b.startAt)));
  }
}

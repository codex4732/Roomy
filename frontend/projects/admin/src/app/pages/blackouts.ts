import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

interface LocationDto { id: string; name: string; }
interface RoomDto { id: string; name: string; }
interface BlackoutDto {
  id: string; locationId: string; roomId: string | null;
  reason: string; startAt: string; endAt: string;
}

@Component({
  selector: 'app-blackouts',
  imports: [FormsModule],
  template: `
    <h2>Blackout periods</h2>
    <p class="sub">Block a room — or a whole location — for maintenance or holidays. Existing bookings in the window are cancelled with the reason.</p>
    @if (notice()) { <p class="notice">{{ notice() }}</p> }

    <div class="card">
      <h3>New blackout</h3>
      <form (ngSubmit)="create()">
        <select name="loc" [ngModel]="locationId()" (ngModelChange)="setLocation($event)">
          @for (l of locations(); track l.id) { <option [value]="l.id">{{ l.name }}</option> }
        </select>
        <select name="room" [(ngModel)]="roomId">
          <option value="">Whole location</option>
          @for (r of rooms(); track r.id) { <option [value]="r.id">{{ r.name }}</option> }
        </select>
        <input name="reason" [(ngModel)]="reason" placeholder="Reason (e.g. maintenance)" required />
        <input name="start" type="datetime-local" [(ngModel)]="start" required />
        <input name="end" type="datetime-local" [(ngModel)]="end" required />
        <button type="submit" [disabled]="!reason || !start || !end">Block</button>
      </form>
    </div>

    <div class="card">
      @if (items().length === 0) {
        <p class="empty">No upcoming blackouts at this location.</p>
      } @else {
        <table>
          <thead><tr><th>Scope</th><th>Reason</th><th>From</th><th>Until</th><th></th></tr></thead>
          <tbody>
            @for (b of items(); track b.id) {
              <tr>
                <td>{{ b.roomId ? roomName(b.roomId) : 'Whole location' }}</td>
                <td>{{ b.reason }}</td>
                <td>{{ fmt(b.startAt) }}</td>
                <td>{{ fmt(b.endAt) }}</td>
                <td><button class="warn" (click)="remove(b)">Remove</button></td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: `
    h2 { margin: 0 0 0.3rem; }
    h3 { margin: 0 0 0.7rem; }
    .sub { color: #69788c; margin: 0 0 1rem; }
    .notice { background: #e7f1fd; color: #1a4480; border-radius: 10px; padding: 0.6rem 1rem; }
    .empty { color: #69788c; padding: 0.5rem; }
    .card { background: #fff; border-radius: 14px; box-shadow: 0 4px 18px rgba(20, 40, 60, 0.07); padding: 1rem 1.2rem; margin-bottom: 1rem; }
    form { display: flex; gap: 0.6rem; flex-wrap: wrap; align-items: center; }
    input, select { padding: 0.5rem 0.6rem; border: 1px solid #d5dce4; border-radius: 8px; background: #fff; }
    table { border-collapse: collapse; width: 100%; }
    th { text-align: left; font-size: 0.72rem; text-transform: uppercase; color: #69788c; padding: 0.4rem 0.6rem; }
    td { padding: 0.5rem 0.6rem; border-top: 1px solid #eef1f4; }
    button { padding: 0.5rem 1rem; border: none; border-radius: 8px; background: #2a5285; color: #fff; cursor: pointer; }
    button:disabled { opacity: 0.5; }
    button.warn { background: #c2402a; padding: 0.35rem 0.8rem; }
  `,
})
export class Blackouts {
  private readonly http = inject(HttpClient);
  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly rooms = signal<RoomDto[]>([]);
  protected readonly items = signal<BlackoutDto[]>([]);
  protected readonly locationId = signal('');
  protected readonly notice = signal<string | null>(null);

  roomId = '';
  reason = '';
  start = '';
  end = '';

  constructor() {
    void firstValueFrom(this.http.get<LocationDto[]>('/api/v1/locations')).then(async (ls) => {
      this.locations.set(ls);
      if (ls.length) {
        await this.setLocation(ls[0].id);
      }
    });
  }

  protected roomName(id: string): string {
    return this.rooms().find((r) => r.id === id)?.name ?? '—';
  }

  protected fmt(iso: string): string {
    return new Date(iso).toLocaleString(undefined, {
      month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  }

  protected async setLocation(id: string): Promise<void> {
    this.locationId.set(id);
    this.roomId = '';
    this.rooms.set(await firstValueFrom(this.http.get<RoomDto[]>(`/api/v1/locations/${id}/rooms`)));
    await this.refresh();
  }

  protected async create(): Promise<void> {
    try {
      const result = await firstValueFrom(this.http.post<{ cancelledBookings: number }>('/api/v1/blackouts', {
        locationId: this.locationId(),
        roomId: this.roomId || null,
        reason: this.reason,
        startAt: new Date(this.start).toISOString(),
        endAt: new Date(this.end).toISOString(),
      }));
      this.notice.set(`Blackout created — ${result.cancelledBookings} existing booking(s) cancelled.`);
      this.reason = this.start = this.end = '';
      await this.refresh();
    } catch (e: unknown) {
      this.notice.set(e instanceof HttpErrorResponse ? (e.error?.detail ?? 'Failed.') : 'Failed.');
    }
  }

  protected async remove(blackout: BlackoutDto): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/v1/blackouts/${blackout.id}`));
    this.notice.set('Blackout removed.');
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    const from = new Date().toISOString();
    const to = new Date(Date.now() + 365 * 24 * 3600_000).toISOString();
    this.items.set(await firstValueFrom(this.http.get<BlackoutDto[]>('/api/v1/blackouts',
      { params: { locationId: this.locationId(), from, to } })));
  }
}

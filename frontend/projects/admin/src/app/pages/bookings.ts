import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

interface LocationDto { id: string; name: string; timezone: string; }
interface BookingDto {
  id: string; roomId: string; title: string; startAt: string; endAt: string;
  status: string; organizerName: string; setupNotes: string | null; participants: string[];
}
interface RoomDto { id: string; name: string; }

@Component({
  selector: 'app-bookings',
  imports: [FormsModule],
  template: `
    <h2>Bookings overview</h2>
    @if (notice()) { <p class="notice">{{ notice() }}</p> }
    <div class="filters">
      <select [ngModel]="locationId()" (ngModelChange)="setLocation($event)">
        @for (l of locations(); track l.id) { <option [value]="l.id">{{ l.name }}</option> }
      </select>
      <input type="date" [ngModel]="date()" (ngModelChange)="setDate($event)" />
    </div>
    <div class="card">
      @if (items().length === 0) {
        <p class="empty">No bookings for this day.</p>
      } @else {
        <table>
          <thead><tr><th>Time</th><th>Room</th><th>Title</th><th>Organizer</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (b of items(); track b.id) {
              <tr>
                <td>{{ fmt(b.startAt) }}–{{ fmt(b.endAt) }}</td>
                <td>{{ roomName(b.roomId) }}</td>
                <td>
                  {{ b.title }}
                  @if (b.participants.length) { <span class="sub">+{{ b.participants.length }}</span> }
                  @if (b.setupNotes) { <div class="setup">🛠 {{ b.setupNotes }}</div> }
                </td>
                <td>{{ b.organizerName }}</td>
                <td><span class="chip">{{ b.status }}</span></td>
                <td>
                  @if (b.status === 'Confirmed' || b.status === 'PendingApproval') {
                    <button class="warn" (click)="cancel(b)">Cancel</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: `
    h2 { margin: 0 0 1rem; }
    .notice { background: #e7f1fd; color: #1a4480; border-radius: 10px; padding: 0.6rem 1rem; }
    .empty { color: #69788c; padding: 1rem; }
    .filters { display: flex; gap: 0.6rem; margin-bottom: 1rem; }
    select, input { padding: 0.5rem 0.7rem; border: 1px solid #d5dce4; border-radius: 8px; background: #fff; }
    .card { background: #fff; border-radius: 14px; box-shadow: 0 4px 18px rgba(20, 40, 60, 0.07); overflow: hidden; }
    table { border-collapse: collapse; width: 100%; }
    th { text-align: left; font-size: 0.72rem; text-transform: uppercase; color: #69788c; padding: 0.8rem 1rem 0.5rem; }
    td { padding: 0.6rem 1rem; border-top: 1px solid #eef1f4; }
    .chip { background: #eef1f4; border-radius: 999px; padding: 0.2rem 0.6rem; font-size: 0.75rem; }
    .sub { color: #69788c; font-size: 0.78rem; margin-left: 0.3rem; }
    .setup { color: #92400e; background: #fffbeb; border-radius: 6px; padding: 0.2rem 0.5rem; font-size: 0.78rem; margin-top: 0.25rem; }
    button.warn { padding: 0.35rem 0.8rem; border: none; border-radius: 8px; background: #c2402a; color: #fff; cursor: pointer; }
  `,
})
export class Bookings {
  private readonly http = inject(HttpClient);
  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly rooms = signal<RoomDto[]>([]);
  protected readonly items = signal<BookingDto[]>([]);
  protected readonly locationId = signal('');
  protected readonly date = signal(new Date().toISOString().slice(0, 10));
  protected readonly notice = signal<string | null>(null);

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
    const tz = this.locations().find((l) => l.id === this.locationId())?.timezone;
    return new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', timeZone: tz });
  }

  protected async setLocation(id: string): Promise<void> {
    this.locationId.set(id);
    this.rooms.set(await firstValueFrom(this.http.get<RoomDto[]>(`/api/v1/locations/${id}/rooms`)));
    await this.refresh();
  }

  protected async setDate(date: string): Promise<void> {
    this.date.set(date);
    await this.refresh();
  }

  protected async cancel(booking: BookingDto): Promise<void> {
    if (!confirm(`Cancel "${booking.title}" by ${booking.organizerName}?`)) {
      return;
    }
    await firstValueFrom(this.http.post(`/api/v1/bookings/${booking.id}/cancel`, {}));
    this.notice.set(`Cancelled "${booking.title}".`);
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    const from = `${this.date()}T00:00:00Z`;
    const to = `${this.date()}T23:59:59Z`;
    this.items.set(await firstValueFrom(this.http.get<BookingDto[]>('/api/v1/bookings',
      { params: { locationId: this.locationId(), from, to } })));
  }
}

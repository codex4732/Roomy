import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface PendingBooking {
  id: string;
  title: string;
  startAt: string;
  endAt: string;
  room: string;
  organizer: string;
  createdAt: string;
  seriesId: string | null;
}

@Component({
  selector: 'app-approvals',
  imports: [],
  template: `
    <h2>Pending approvals</h2>
    @if (notice()) { <p class="notice">{{ notice() }}</p> }
    @if (items().length === 0) {
      <p class="empty">Nothing waiting — all caught up. ✓</p>
    } @else {
      <div class="card">
        <table>
          <thead>
            <tr><th>When</th><th>Room</th><th>Title</th><th>Requested by</th><th></th></tr>
          </thead>
          <tbody>
            @for (item of items(); track item.id) {
              <tr>
                <td>{{ fmt(item.startAt) }} – {{ fmtTime(item.endAt) }}@if (item.seriesId) { <span class="badge">series</span> }</td>
                <td>{{ item.room }}</td>
                <td>{{ item.title }}</td>
                <td>{{ item.organizer }}</td>
                <td class="actions">
                  <button (click)="decide(item, true)">Approve</button>
                  <button class="warn" (click)="decide(item, false)">Decline</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `
    h2 { margin: 0 0 1rem; }
    .empty { color: #69788c; }
    .notice { background: #e7f1fd; color: #1a4480; border-radius: 10px; padding: 0.6rem 1rem; }
    .card { background: #fff; border-radius: 14px; box-shadow: 0 4px 18px rgba(20, 40, 60, 0.07); overflow: hidden; }
    table { border-collapse: collapse; width: 100%; }
    th { text-align: left; font-size: 0.72rem; text-transform: uppercase; letter-spacing: 0.04em; color: #69788c; padding: 0.8rem 1rem 0.5rem; }
    td { padding: 0.65rem 1rem; border-top: 1px solid #eef1f4; }
    .badge { background: #fff1cc; color: #8a6d00; border-radius: 999px; padding: 0.05rem 0.5rem; font-size: 0.68rem; margin-left: 0.4rem; }
    .actions { display: flex; gap: 0.4rem; }
    button { padding: 0.4rem 0.9rem; border: none; border-radius: 8px; background: #15806b; color: #fff; cursor: pointer; }
    button.warn { background: #c2402a; }
  `,
})
export class Approvals {
  private readonly http = inject(HttpClient);
  protected readonly items = signal<PendingBooking[]>([]);
  protected readonly notice = signal<string | null>(null);

  constructor() {
    void this.refresh();
  }

  protected fmt(iso: string): string {
    return new Date(iso).toLocaleString(undefined, {
      weekday: 'short', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  }

  protected fmtTime(iso: string): string {
    return new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  }

  protected async decide(item: PendingBooking, approve: boolean): Promise<void> {
    const action = approve ? 'approve' : 'decline';
    try {
      await firstValueFrom(this.http.post(`/api/v1/approvals/${item.id}/${action}`,
        approve ? {} : { reason: 'Declined by facility staff' }));
      this.notice.set(`${approve ? 'Approved' : 'Declined'} "${item.title}".`);
      await this.refresh();
    } catch {
      this.notice.set('Action failed — it may already be decided.');
      await this.refresh();
    }
  }

  private async refresh(): Promise<void> {
    this.items.set(await firstValueFrom(this.http.get<PendingBooking[]>('/api/v1/approvals')));
  }
}

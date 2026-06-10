import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

interface TenantSettings {
  bookingWindowDays: number;
  maxDurationMinutes: number;
  minDurationMinutes: number;
  maxActiveBookingsPerUser: number;
  checkinGraceMinutes: number;
  approvalExpiryHours: number;
}

@Component({
  selector: 'app-settings',
  imports: [FormsModule],
  template: `
    <h2>Booking policies</h2>
    @if (notice()) { <p class="notice">{{ notice() }}</p> }
    @if (model(); as m) {
      <div class="card">
        <form (ngSubmit)="save()">
          <label>Booking window (days ahead)
            <input type="number" name="window" min="1" max="365" [(ngModel)]="m.bookingWindowDays" />
          </label>
          <label>Max duration (minutes)
            <input type="number" name="max" min="15" max="44640" [(ngModel)]="m.maxDurationMinutes" />
            <span class="sub">{{ (m.maxDurationMinutes / 1440).toFixed(1) }} days</span>
          </label>
          <label>Min duration (minutes)
            <input type="number" name="min" min="5" [(ngModel)]="m.minDurationMinutes" />
          </label>
          <label>Max active bookings per user
            <input type="number" name="quota" min="1" max="100" [(ngModel)]="m.maxActiveBookingsPerUser" />
          </label>
          <label>Check-in grace (minutes)
            <input type="number" name="grace" min="5" max="30" [(ngModel)]="m.checkinGraceMinutes" />
          </label>
          <label>Approval auto-expiry (hours)
            <input type="number" name="expiry" min="1" max="168" [(ngModel)]="m.approvalExpiryHours" />
          </label>
          <button type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save policies' }}</button>
        </form>
      </div>
    }
  `,
  styles: `
    h2 { margin: 0 0 1rem; }
    .notice { background: #e7f1fd; color: #1a4480; border-radius: 10px; padding: 0.6rem 1rem; }
    .card { background: #fff; border-radius: 14px; box-shadow: 0 4px 18px rgba(20, 40, 60, 0.07); padding: 1.2rem 1.4rem; max-width: 460px; }
    form { display: flex; flex-direction: column; gap: 0.9rem; }
    label { display: flex; flex-direction: column; gap: 0.3rem; font-size: 0.88rem; font-weight: 600; color: #44546a; }
    input { padding: 0.5rem 0.6rem; border: 1px solid #d5dce4; border-radius: 8px; font-size: 0.95rem; }
    .sub { color: #69788c; font-weight: 400; font-size: 0.78rem; }
    button { padding: 0.6rem 1.2rem; border: none; border-radius: 8px; background: #2a5285; color: #fff; cursor: pointer; align-self: start; }
    button:disabled { opacity: 0.5; }
  `,
})
export class Settings {
  private readonly http = inject(HttpClient);
  protected readonly model = signal<TenantSettings | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly saving = signal(false);

  constructor() {
    void firstValueFrom(this.http.get<TenantSettings>('/api/v1/settings'))
      .then((s) => this.model.set(s));
  }

  protected async save(): Promise<void> {
    const model = this.model();
    if (!model) {
      return;
    }
    this.saving.set(true);
    try {
      this.model.set(await firstValueFrom(this.http.put<TenantSettings>('/api/v1/settings', model)));
      this.notice.set('Policies saved.');
    } catch (e: unknown) {
      this.notice.set(e instanceof HttpErrorResponse ? (e.error?.detail ?? 'Save failed.') : 'Save failed.');
    } finally {
      this.saving.set(false);
    }
  }
}

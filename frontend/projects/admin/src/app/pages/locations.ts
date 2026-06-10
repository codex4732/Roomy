import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

interface LocationDto { id: string; name: string; timezone: string; address: string | null; }
interface RoomDto {
  id: string; name: string; capacity: number; floor: string | null;
  requiresApproval: boolean; checkinRequired: boolean; status: string;
}

@Component({
  selector: 'app-locations',
  imports: [FormsModule],
  template: `
    <h2>Locations &amp; rooms</h2>
    @if (notice()) { <p class="notice">{{ notice() }}</p> }

    <div class="card form-card">
      <h3>Add location</h3>
      <form (ngSubmit)="addLocation()">
        <input name="lname" [(ngModel)]="locName" placeholder="Name" required />
        <input name="ltz" [(ngModel)]="locTz" placeholder="IANA time zone, e.g. Europe/Paris" required />
        <input name="laddr" [(ngModel)]="locAddr" placeholder="Address (optional)" />
        <button type="submit" [disabled]="!locName || !locTz">Add</button>
      </form>
    </div>

    @for (location of locations(); track location.id) {
      <div class="card">
        <div class="loc-head">
          <div>
            <h3>{{ location.name }}</h3>
            <p class="meta">{{ location.timezone }} @if (location.address) { · {{ location.address }} }</p>
          </div>
        </div>
        <table>
          <thead>
            <tr><th>Room</th><th>Capacity</th><th>Floor</th><th>Approval</th><th>Check-in</th><th>Status</th></tr>
          </thead>
          <tbody>
            @for (room of roomsOf(location.id); track room.id) {
              <tr>
                <td>{{ room.name }}</td>
                <td>{{ room.capacity }}</td>
                <td>{{ room.floor ?? '—' }}</td>
                <td><input type="checkbox" [checked]="room.requiresApproval" (change)="patchRoom(room, { requiresApproval: !room.requiresApproval })" /></td>
                <td><input type="checkbox" [checked]="room.checkinRequired" (change)="patchRoom(room, { checkinRequired: !room.checkinRequired })" /></td>
                <td>
                  <select [ngModel]="room.status" (ngModelChange)="patchRoom(room, { status: $event })">
                    <option value="Active">Active</option>
                    <option value="TemporarilyUnavailable">Unavailable</option>
                    <option value="Retired">Retired</option>
                  </select>
                </td>
              </tr>
            }
          </tbody>
        </table>
        <form class="room-form" (ngSubmit)="addRoom(location.id)">
          <input name="rname-{{ location.id }}" [(ngModel)]="roomName" placeholder="New room name" />
          <input name="rcap-{{ location.id }}" type="number" min="1" [(ngModel)]="roomCapacity" placeholder="Seats" />
          <input name="rfloor-{{ location.id }}" [(ngModel)]="roomFloor" placeholder="Floor" />
          <label><input name="rappr-{{ location.id }}" type="checkbox" [(ngModel)]="roomApproval" /> needs approval</label>
          <button type="submit" [disabled]="!roomName">Add room</button>
        </form>
      </div>
    }
  `,
  styles: `
    h2 { margin: 0 0 1rem; }
    h3 { margin: 0 0 0.3rem; }
    .meta { color: #69788c; margin: 0; font-size: 0.85rem; }
    .notice { background: #e7f1fd; color: #1a4480; border-radius: 10px; padding: 0.6rem 1rem; }
    .card { background: #fff; border-radius: 14px; box-shadow: 0 4px 18px rgba(20, 40, 60, 0.07); padding: 1rem 1.2rem; margin-bottom: 1rem; }
    table { border-collapse: collapse; width: 100%; margin: 0.6rem 0; }
    th { text-align: left; font-size: 0.72rem; text-transform: uppercase; color: #69788c; padding: 0.4rem 0.6rem; }
    td { padding: 0.45rem 0.6rem; border-top: 1px solid #eef1f4; }
    form { display: flex; gap: 0.6rem; align-items: center; flex-wrap: wrap; }
    input:not([type='checkbox']), select { padding: 0.5rem 0.6rem; border: 1px solid #d5dce4; border-radius: 8px; }
    input[type='number'] { width: 80px; }
    button { padding: 0.5rem 1rem; border: none; border-radius: 8px; background: #2a5285; color: #fff; cursor: pointer; }
    button:disabled { opacity: 0.5; }
    label { font-size: 0.85rem; color: #44546a; }
  `,
})
export class Locations {
  private readonly http = inject(HttpClient);
  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly rooms = signal<Map<string, RoomDto[]>>(new Map());
  protected readonly notice = signal<string | null>(null);

  locName = ''; locTz = ''; locAddr = '';
  roomName = ''; roomCapacity = 4; roomFloor = ''; roomApproval = false;

  constructor() {
    void this.refresh();
  }

  protected roomsOf(locationId: string): RoomDto[] {
    return this.rooms().get(locationId) ?? [];
  }

  protected async addLocation(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/v1/locations',
        { name: this.locName, timezone: this.locTz, address: this.locAddr || null }));
      this.notice.set(`Added location "${this.locName}".`);
      this.locName = this.locTz = this.locAddr = '';
      await this.refresh();
    } catch (e: unknown) {
      this.notice.set(e instanceof HttpErrorResponse ? (e.error?.detail ?? 'Failed.') : 'Failed.');
    }
  }

  protected async addRoom(locationId: string): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`/api/v1/locations/${locationId}/rooms`, {
        name: this.roomName, capacity: this.roomCapacity, floor: this.roomFloor || null,
        requiresApproval: this.roomApproval, checkinRequired: true,
      }));
      this.notice.set(`Added room "${this.roomName}".`);
      this.roomName = ''; this.roomFloor = ''; this.roomApproval = false; this.roomCapacity = 4;
      await this.refresh();
    } catch (e: unknown) {
      this.notice.set(e instanceof HttpErrorResponse ? (e.error?.detail ?? 'Failed.') : 'Failed.');
    }
  }

  protected async patchRoom(room: RoomDto, patch: Partial<RoomDto>): Promise<void> {
    try {
      await firstValueFrom(this.http.patch(`/api/v1/rooms/${room.id}`, patch));
      await this.refresh();
    } catch {
      this.notice.set('Update failed.');
    }
  }

  private async refresh(): Promise<void> {
    const locations = await firstValueFrom(this.http.get<LocationDto[]>('/api/v1/locations'));
    this.locations.set(locations);
    const map = new Map<string, RoomDto[]>();
    await Promise.all(locations.map(async (l) => {
      map.set(l.id, await firstValueFrom(this.http.get<RoomDto[]>(`/api/v1/locations/${l.id}/rooms`)));
    }));
    this.rooms.set(map);
  }
}

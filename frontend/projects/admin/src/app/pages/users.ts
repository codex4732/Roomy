import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

interface UserDto {
  id: string; email: string; name: string; role: string; status: string; noShowCount: number;
}

@Component({
  selector: 'app-users',
  imports: [FormsModule],
  template: `
    <h2>Users</h2>
    @if (notice()) { <p class="notice">{{ notice() }}</p> }

    <div class="card form-card">
      <h3>Add user</h3>
      <form (ngSubmit)="add()">
        <input name="name" [(ngModel)]="name" placeholder="Full name" required />
        <input name="email" type="email" [(ngModel)]="email" placeholder="Email" required />
        <select name="role" [(ngModel)]="role">
          <option value="Member">Member</option>
          <option value="FacilityManager">Facility Manager</option>
          <option value="TenantAdmin">Tenant Admin</option>
        </select>
        <input name="password" type="password" [(ngModel)]="password" placeholder="Temp password (12+ chars)" required />
        <button type="submit" [disabled]="!name || !email || password.length < 12">Add</button>
      </form>
    </div>

    <div class="card">
      <table>
        <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th>No-shows</th></tr></thead>
        <tbody>
          @for (user of users(); track user.id) {
            <tr>
              <td>{{ user.name }}</td>
              <td>{{ user.email }}</td>
              <td>
                <select [ngModel]="user.role" (ngModelChange)="patch(user, { role: $event })">
                  <option value="Member">Member</option>
                  <option value="FacilityManager">Facility Manager</option>
                  <option value="TenantAdmin">Tenant Admin</option>
                </select>
              </td>
              <td>
                <select [ngModel]="user.status" (ngModelChange)="patch(user, { status: $event })">
                  <option value="Active">Active</option>
                  <option value="Inactive">Inactive</option>
                </select>
              </td>
              <td>{{ user.noShowCount }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `
    h2 { margin: 0 0 1rem; }
    h3 { margin: 0 0 0.6rem; }
    .notice { background: #e7f1fd; color: #1a4480; border-radius: 10px; padding: 0.6rem 1rem; }
    .card { background: #fff; border-radius: 14px; box-shadow: 0 4px 18px rgba(20, 40, 60, 0.07); padding: 1rem 1.2rem; margin-bottom: 1rem; }
    table { border-collapse: collapse; width: 100%; }
    th { text-align: left; font-size: 0.72rem; text-transform: uppercase; color: #69788c; padding: 0.4rem 0.6rem; }
    td { padding: 0.45rem 0.6rem; border-top: 1px solid #eef1f4; }
    form { display: flex; gap: 0.6rem; flex-wrap: wrap; }
    input, select { padding: 0.5rem 0.6rem; border: 1px solid #d5dce4; border-radius: 8px; }
    button { padding: 0.5rem 1rem; border: none; border-radius: 8px; background: #2a5285; color: #fff; cursor: pointer; }
    button:disabled { opacity: 0.5; }
  `,
})
export class Users {
  private readonly http = inject(HttpClient);
  protected readonly users = signal<UserDto[]>([]);
  protected readonly notice = signal<string | null>(null);

  name = ''; email = ''; role = 'Member'; password = '';

  constructor() {
    void this.refresh();
  }

  protected async add(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/v1/users',
        { name: this.name, email: this.email, role: this.role, password: this.password }));
      this.notice.set(`Added ${this.name}.`);
      this.name = this.email = this.password = ''; this.role = 'Member';
      await this.refresh();
    } catch (e: unknown) {
      this.notice.set(e instanceof HttpErrorResponse ? (e.error?.detail ?? 'Failed.') : 'Failed.');
    }
  }

  protected async patch(user: UserDto, patch: { role?: string; status?: string }): Promise<void> {
    try {
      await firstValueFrom(this.http.patch(`/api/v1/users/${user.id}`, patch));
      await this.refresh();
    } catch {
      this.notice.set('Update failed.');
      await this.refresh();
    }
  }

  private async refresh(): Promise<void> {
    this.users.set(await firstValueFrom(this.http.get<UserDto[]>('/api/v1/users')));
  }
}

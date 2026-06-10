import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

export interface SessionUser {
  id: string;
  email: string;
  name: string;
  role: string;
}

interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  user: SessionUser;
}

interface StoredSession {
  tenant: string;
  accessToken: string;
  user: SessionUser;
}

const STORAGE_KEY = 'roomy_session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<StoredSession | null>(readStoredSession());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly tenant = computed(() => this.session()?.tenant ?? null);
  readonly accessToken = computed(() => this.session()?.accessToken ?? null);
  readonly isLoggedIn = computed(() => this.session() !== null);

  async login(tenant: string, email: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(
        '/api/v1/auth/login',
        { email, password },
        { headers: { 'X-Roomy-Tenant': tenant } },
      ),
    );
    const session: StoredSession = { tenant, accessToken: response.accessToken, user: response.user };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    this.session.set(session);
  }

  async logout(): Promise<void> {
    const tenant = this.tenant();
    this.clearSession();
    if (tenant) {
      try {
        await firstValueFrom(
          this.http.post('/api/v1/auth/logout', {}, { headers: { 'X-Roomy-Tenant': tenant } }),
        );
      } catch {
        // Session is already cleared locally; server-side revocation is best-effort here.
      }
    }
    await this.router.navigateByUrl('/login');
  }

  clearSession(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.session.set(null);
  }
}

function readStoredSession(): StoredSession | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as StoredSession) : null;
  } catch {
    return null;
  }
}

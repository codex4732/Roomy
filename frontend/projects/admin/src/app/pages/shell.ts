import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <h1><span class="logo">◴</span> Roomy Admin</h1>
      <nav>
        <a routerLink="/approvals" routerLinkActive="active">Approvals</a>
        <a routerLink="/locations" routerLinkActive="active">Locations</a>
        <a routerLink="/users" routerLinkActive="active">Users</a>
      </nav>
      <div class="user">
        <span>{{ auth.user()?.name }}</span>
        <button type="button" (click)="auth.logout()">Sign out</button>
      </div>
    </header>
    <main class="content"><router-outlet /></main>
  `,
  styles: `
    .topbar {
      display: flex;
      align-items: center;
      gap: 2rem;
      padding: 0.85rem 1.6rem;
      background: linear-gradient(120deg, #1f3a5f, #2a5285 60%, #356aa8);
      color: #fff;
      box-shadow: 0 2px 12px rgba(31, 58, 95, 0.3);

      h1 { margin: 0; font-size: 1.25rem; display: flex; gap: 0.45rem; align-items: center; }
      .logo { font-size: 1.4rem; }

      nav {
        display: flex;
        gap: 0.4rem;
        flex: 1;

        a {
          color: rgba(255, 255, 255, 0.85);
          text-decoration: none;
          padding: 0.45rem 0.95rem;
          border-radius: 8px;
          transition: background 0.15s;

          &:hover { background: rgba(255, 255, 255, 0.12); }
          &.active { background: rgba(255, 255, 255, 0.2); color: #fff; }
        }
      }

      .user {
        display: flex;
        align-items: center;
        gap: 0.7rem;

        button {
          background: rgba(255, 255, 255, 0.12);
          border: 1px solid rgba(255, 255, 255, 0.45);
          color: #fff;
          border-radius: 8px;
          padding: 0.4rem 0.9rem;
          cursor: pointer;
        }
      }
    }

    .content { padding: 1.5rem; max-width: 1100px; margin: 0 auto; }
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
}

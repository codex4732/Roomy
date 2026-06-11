import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  tenant = 'demo';
  email = '';
  password = '';

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.tenant.trim().toLowerCase(), this.email.trim(), this.password);
      await this.router.navigateByUrl('/');
    } catch {
      this.error.set('Sign-in failed. Check the organization, email, and password.');
    } finally {
      this.busy.set(false);
    }
  }
}

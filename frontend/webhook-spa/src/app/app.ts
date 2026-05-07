import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, Router } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ThemeService } from './services/theme.service';
import { AuthService } from './core/services/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, MatToolbarModule, MatIconModule, MatButtonModule, MatTooltipModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly theme = inject(ThemeService);
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  async logout(): Promise<void> {
    await this.auth.logout();
    this.router.navigate(['/login']);
  }
}

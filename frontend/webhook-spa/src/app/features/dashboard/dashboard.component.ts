import { Component, inject, signal, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { TokenService } from '../../core/services/token.service';
import { Token } from '../../core/models/token.model';
import { CreateTokenDialogComponent } from './create-token-dialog.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    DatePipe,
    MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatTooltipModule,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  private readonly tokenService = inject(TokenService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly tokens = signal<Token[]>([]);
  readonly loading = signal(false);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.tokenService.getTokens().subscribe({
      next: tokens => { this.tokens.set(tokens); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    const ref = this.dialog.open(CreateTokenDialogComponent, { width: '400px' });
    ref.afterClosed().subscribe((description: string | undefined) => {
      if (description === undefined) return;
      this.tokenService.createToken(description || undefined).subscribe(token => {
        this.tokens.update(list => [token, ...list]);
        this.snackBar.open('Webhook URL created', 'OK', { duration: 3000 });
      });
    });
  }

  open(token: Token): void {
    this.router.navigate(['/tokens', token.id]);
  }

  delete(token: Token, event: MouseEvent): void {
    event.stopPropagation();
    if (!confirm('Delete this webhook URL? All captured requests will be removed.')) return;
    this.tokenService.deleteToken(token.id).subscribe(() => {
      this.tokens.update(list => list.filter(t => t.id !== token.id));
      this.snackBar.open('Deleted', 'OK', { duration: 3000 });
    });
  }

  copyUrl(token: Token, event: MouseEvent): void {
    event.stopPropagation();
    navigator.clipboard.writeText(token.webhookUrl).then(() =>
      this.snackBar.open('URL copied to clipboard', 'OK', { duration: 2000 })
    );
  }
}

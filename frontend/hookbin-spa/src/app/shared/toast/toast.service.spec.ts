import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ToastService);
    document.querySelectorAll('.toast').forEach((el) => el.remove());
  });

  afterEach(() => {
    document.querySelectorAll('.toast').forEach((el) => el.remove());
  });

  it('appends a toast element with the given message', () => {
    service.show('Saved');

    const toasts = document.querySelectorAll('.toast');
    expect(toasts.length).toBe(1);
    expect(toasts[0].textContent).toBe('Saved');
  });

  it('two consecutive shows produce two toast elements', () => {
    service.show('First');
    service.show('Second');

    const toasts = Array.from(document.querySelectorAll('.toast'));
    expect(toasts.length).toBe(2);
    expect(toasts.map((t) => t.textContent)).toEqual(['First', 'Second']);
  });

  it('removes the visible class after the duration elapses', () => {
    vi.useFakeTimers();
    try {
      service.show('Times-out', 100);

      const toast = document.querySelector('.toast')!;
      vi.advanceTimersToNextTimer();
      expect(toast.classList.contains('toast--visible')).toBe(true);

      vi.advanceTimersByTime(100);
      expect(toast.classList.contains('toast--visible')).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });

  it('uses a longer default duration than an explicit short duration', () => {
    vi.useFakeTimers();
    try {
      service.show('Default duration');
      const toast = document.querySelector('.toast')!;
      vi.advanceTimersToNextTimer();
      expect(toast.classList.contains('toast--visible')).toBe(true);

      // 1s is well below the default of 3s — the toast must still be visible.
      vi.advanceTimersByTime(1000);
      expect(toast.classList.contains('toast--visible')).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });
});

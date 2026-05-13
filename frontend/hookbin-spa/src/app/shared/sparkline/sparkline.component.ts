import { Component, input, computed } from '@angular/core';

interface Bar {
  height: string;
  opacity: string;
  color: string;
}

@Component({
  selector: 'app-sparkline',
  standalone: true,
  template: `
    <div class="sparkline">
      @for (bar of bars(); track $index) {
        <div
          class="bar"
          [style.height]="bar.height"
          [style.opacity]="bar.opacity"
          [style.background]="bar.color"
        ></div>
      }
    </div>
  `,
  styles: [
    `
      .sparkline {
        display: flex;
        align-items: flex-end;
        gap: 2px;
        height: 28px;
        width: 100%;
      }

      .bar {
        flex: 1;
        border-radius: 1px;
        min-height: 2px;
        transition:
          height 200ms ease,
          opacity 200ms ease;
      }
    `,
  ],
})
export class SparklineComponent {
  readonly bins = input.required<number[]>();

  readonly bars = computed<Bar[]>(() => {
    const data = this.bins();
    const max = Math.max(...data, 1);
    return data.map((n) => {
      if (n === 0) {
        return { height: '2px', opacity: '1', color: 'var(--border)' };
      }
      const pct = (n / max) * 100;
      const opacity = 0.3 + (n / max) * 0.6;
      return {
        height: `${Math.max(pct, 8)}%`,
        opacity: opacity.toFixed(3),
        color: 'var(--accent)',
      };
    });
  });
}

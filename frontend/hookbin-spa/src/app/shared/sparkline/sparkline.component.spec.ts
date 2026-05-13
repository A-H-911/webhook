import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { SparklineComponent } from './sparkline.component';

@Component({
  standalone: true,
  imports: [SparklineComponent],
  template: `<app-sparkline [bins]="data()" />`,
})
class HostComponent {
  data = signal<number[]>([]);
}

function makeFixture(initial: number[]): {
  fixture: ComponentFixture<HostComponent>;
  host: HostComponent;
} {
  TestBed.configureTestingModule({ imports: [HostComponent] });
  const fixture = TestBed.createComponent(HostComponent);
  const host = fixture.componentInstance;
  host.data.set(initial);
  fixture.detectChanges();
  return { fixture, host };
}

describe('SparklineComponent', () => {
  it('renders one bar per input value', () => {
    const { fixture } = makeFixture([1, 2, 3, 4]);

    const bars = fixture.nativeElement.querySelectorAll('.bar');
    expect(bars.length).toBe(4);
  });

  it('renders zero bars for empty input', () => {
    const { fixture } = makeFixture([]);

    const bars = fixture.nativeElement.querySelectorAll('.bar');
    expect(bars.length).toBe(0);
  });

  it('uses border colour for zero-valued bars', () => {
    const { fixture } = makeFixture([0, 0, 0]);

    const bars = fixture.nativeElement.querySelectorAll('.bar') as NodeListOf<HTMLElement>;
    bars.forEach((bar) => {
      expect(bar.style.background).toContain('border');
    });
  });

  it('uses accent colour for non-zero bars', () => {
    const { fixture } = makeFixture([1, 2, 3]);

    const bars = fixture.nativeElement.querySelectorAll('.bar') as NodeListOf<HTMLElement>;
    bars.forEach((bar) => {
      expect(bar.style.background).toContain('accent');
    });
  });

  it('updates rendering when the signal input changes', () => {
    const { fixture, host } = makeFixture([1]);
    expect(fixture.nativeElement.querySelectorAll('.bar').length).toBe(1);

    host.data.set([1, 2, 3, 4, 5]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.bar').length).toBe(5);
  });
});

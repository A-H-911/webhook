import { ComponentFixture, TestBed } from '@angular/core/testing';
import { JsonTreeComponent } from './json-tree.component';

describe('JsonTreeComponent', () => {
  let fixture: ComponentFixture<JsonTreeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [JsonTreeComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(JsonTreeComponent);
  });

  function setValue(value: unknown): void {
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
  }

  function rowTexts(): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.json-row')).map((el: any) =>
      el.textContent.trim(),
    );
  }

  it('renders a string primitive with .str class', () => {
    setValue('hello');
    const span = fixture.nativeElement.querySelector('span.str');
    expect(span).toBeTruthy();
    expect(span.textContent).toBe('"hello"');
  });

  it('renders a number with .num class', () => {
    setValue(42);
    expect(fixture.nativeElement.querySelector('span.num')?.textContent).toBe('42');
  });

  it('renders a boolean with .bool class and null with .null class', () => {
    setValue(true);
    expect(fixture.nativeElement.querySelector('span.bool')?.textContent).toBe('true');
    setValue(null);
    expect(fixture.nativeElement.querySelector('span.null')?.textContent).toBe('null');
  });

  it('renders nested objects with toggleable rows', () => {
    setValue({ a: { b: 1 } });
    const toggles = fixture.nativeElement.querySelectorAll('button.json-toggle');
    expect(toggles.length).toBe(2);
  });

  it('collapses an array node and shows preview text', () => {
    setValue({ items: [1, 2, 3] });
    const toggles = fixture.nativeElement.querySelectorAll('button.json-toggle');
    const arrayToggle = Array.from(toggles).find((t: any) =>
      t.parentElement?.textContent?.includes('items'),
    ) as HTMLButtonElement;
    arrayToggle.click();
    fixture.detectChanges();
    const collapsed = rowTexts().find((t: string) => t.includes('items'));
    expect(collapsed).toContain('... 3 items');
  });

  it('collapses an object node and shows preview text', () => {
    setValue({ a: 1, b: 2, c: 3 });
    const rootToggle = fixture.nativeElement.querySelector(
      'button.json-toggle',
    ) as HTMLButtonElement;
    rootToggle.click();
    fixture.detectChanges();
    const collapsed = rowTexts()[0];
    expect(collapsed).toContain('... 3 keys');
  });

  it('renders empty object as inline {}', () => {
    setValue({});
    expect(fixture.nativeElement.querySelector('span.null')?.textContent).toBe('{}');
  });

  it('auto-collapses arrays longer than 20', () => {
    const big = Array.from({ length: 25 }, (_, i) => i);
    setValue({ list: big });
    const rows = rowTexts();
    expect(rows.some((r: string) => r.includes('... 25 items'))).toBe(true);
  });

  it('filter highlights matching rows and hides non-matches', () => {
    setValue({ user: 'alice', role: 'admin', age: 30 });
    fixture.componentRef.setInput('filter', 'alice');
    fixture.detectChanges();
    const matched = fixture.nativeElement.querySelectorAll('.json-row--match');
    expect(matched.length).toBeGreaterThan(0);
    const allText = rowTexts().join(' ');
    expect(allText).toContain('"alice"');
    expect(allText).not.toContain('"admin"');
  });

  it('autoExpandTo expands ancestors of a target path', () => {
    setValue({ a: { b: { c: { d: 1 } } } });
    fixture.componentRef.setInput('autoExpandTo', '/a/b/c');
    fixture.detectChanges();
    expect(rowTexts().some((r: string) => r.includes('1'))).toBe(true);
  });
});

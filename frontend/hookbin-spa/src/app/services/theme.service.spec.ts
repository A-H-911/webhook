import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  let getItem: ReturnType<typeof vi.fn>;
  let setItem: ReturnType<typeof vi.fn>;

  function setup(storedValue: string | null = null): ThemeService {
    document.documentElement.removeAttribute('data-theme');
    getItem = vi.fn().mockReturnValue(storedValue);
    setItem = vi.fn();
    vi.stubGlobal('localStorage', { getItem, setItem });
    TestBed.configureTestingModule({});
    return TestBed.inject(ThemeService);
  }

  afterEach(() => {
    document.documentElement.removeAttribute('data-theme');
    vi.unstubAllGlobals();
  });

  it('defaults to dark when no preference is stored', () => {
    const service = setup(null);
    expect(service.isDark()).toBe(true);
  });

  it('applies dark data-theme attribute to <html> on init when default', () => {
    setup(null);
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('respects stored light preference', () => {
    const service = setup('light');
    expect(service.isDark()).toBe(false);
  });

  it('applies light data-theme attribute when stored preference is light', () => {
    setup('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('toggle switches from dark to light', () => {
    const service = setup(null);
    service.toggle();
    expect(service.isDark()).toBe(false);
  });

  it('toggle sets light data-theme attribute after toggling from dark', () => {
    const service = setup(null);
    service.toggle();
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('toggle writes updated preference to localStorage', () => {
    const service = setup(null);
    service.toggle();
    expect(setItem).toHaveBeenCalledWith('color-scheme', 'light');
  });

  it('toggle from light back to dark restores dark data-theme attribute', () => {
    const service = setup('light');
    service.toggle();
    expect(service.isDark()).toBe(true);
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('falls back to dark when localStorage.getItem throws', () => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockImplementation(() => {
        throw new Error('quota exceeded');
      }),
      setItem: vi.fn(),
    });
    TestBed.configureTestingModule({});
    const service = TestBed.inject(ThemeService);
    expect(service.isDark()).toBe(true);
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });
});

import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  let getItem: ReturnType<typeof vi.fn>;
  let setItem: ReturnType<typeof vi.fn>;

  function setup(storedValue: string | null = null): ThemeService {
    document.documentElement.classList.remove('dark-theme');
    getItem = vi.fn().mockReturnValue(storedValue);
    setItem = vi.fn();
    vi.stubGlobal('localStorage', { getItem, setItem });
    TestBed.configureTestingModule({});
    return TestBed.inject(ThemeService);
  }

  afterEach(() => {
    document.documentElement.classList.remove('dark-theme');
    vi.unstubAllGlobals();
  });

  it('defaults to dark when no preference is stored', () => {
    const service = setup(null);
    expect(service.isDark()).toBe(true);
  });

  it('applies dark-theme class to <html> on init when default', () => {
    setup(null);
    expect(document.documentElement.classList.contains('dark-theme')).toBe(true);
  });

  it('respects stored light preference', () => {
    const service = setup('light');
    expect(service.isDark()).toBe(false);
  });

  it('does not apply dark-theme class when stored preference is light', () => {
    setup('light');
    expect(document.documentElement.classList.contains('dark-theme')).toBe(false);
  });

  it('toggle switches from dark to light', () => {
    const service = setup(null);
    service.toggle();
    expect(service.isDark()).toBe(false);
  });

  it('toggle removes dark-theme class from <html>', () => {
    const service = setup(null);
    service.toggle();
    expect(document.documentElement.classList.contains('dark-theme')).toBe(false);
  });

  it('toggle writes updated preference to localStorage', () => {
    const service = setup(null);
    service.toggle();
    expect(setItem).toHaveBeenCalledWith('color-scheme', 'light');
  });

  it('toggle from light back to dark restores dark-theme class', () => {
    const service = setup('light');
    service.toggle();
    expect(service.isDark()).toBe(true);
    expect(document.documentElement.classList.contains('dark-theme')).toBe(true);
  });

  it('falls back to dark when localStorage.getItem throws', () => {
    document.documentElement.classList.remove('dark-theme');
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockImplementation(() => {
        throw new Error('quota exceeded');
      }),
      setItem: vi.fn(),
    });
    TestBed.configureTestingModule({});
    const service = TestBed.inject(ThemeService);
    expect(service.isDark()).toBe(true);
    expect(document.documentElement.classList.contains('dark-theme')).toBe(true);
  });
});

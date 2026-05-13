import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Router } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthService } from '../../core/services/auth.service';

class FakeAuthService {
  login = vi.fn();
}

class FakeRouter {
  navigate = vi.fn().mockResolvedValue(true);
}

function setup() {
  const auth = new FakeAuthService();
  const router = new FakeRouter();
  TestBed.configureTestingModule({
    imports: [LoginComponent],
    providers: [
      { provide: AuthService, useValue: auth },
      { provide: Router, useValue: router },
    ],
  });
  const fixture: ComponentFixture<LoginComponent> = TestBed.createComponent(LoginComponent);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, auth, router };
}

describe('LoginComponent', () => {
  it('starts in default state', () => {
    const { component } = setup();

    expect(component.username).toBe('');
    expect(component.password).toBe('');
    expect(component.loading()).toBe(false);
    expect(component.error()).toBe('');
    expect(component.hidePassword()).toBe(true);
  });

  it('does not submit when fields are empty', async () => {
    const { component, auth, router } = setup();

    await component.onSubmit();

    expect(auth.login).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does not submit when password is missing', async () => {
    const { component, auth } = setup();
    component.username = 'admin';
    component.password = '';

    await component.onSubmit();

    expect(auth.login).not.toHaveBeenCalled();
  });

  it('calls AuthService.login then navigates to /dashboard on success', async () => {
    const { component, auth, router } = setup();
    auth.login.mockResolvedValue(undefined);
    component.username = 'admin';
    component.password = 'pw';

    await component.onSubmit();

    expect(auth.login).toHaveBeenCalledWith('admin', 'pw');
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
    expect(component.error()).toBe('');
  });

  it('sets error and stays on page when login fails', async () => {
    const { component, auth, router } = setup();
    auth.login.mockRejectedValue(new Error('401'));
    component.username = 'admin';
    component.password = 'wrong';

    await component.onSubmit();

    expect(component.error()).toContain('Invalid');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('clears loading flag after success', async () => {
    const { component, auth } = setup();
    auth.login.mockResolvedValue(undefined);
    component.username = 'a';
    component.password = 'b';

    await component.onSubmit();

    expect(component.loading()).toBe(false);
  });

  it('clears loading flag after failure', async () => {
    const { component, auth } = setup();
    auth.login.mockRejectedValue(new Error());
    component.username = 'a';
    component.password = 'b';

    await component.onSubmit();

    expect(component.loading()).toBe(false);
  });

  it('hidePassword toggles independently of submission state', () => {
    const { component } = setup();

    component.hidePassword.set(false);
    expect(component.hidePassword()).toBe(false);

    component.hidePassword.set(true);
    expect(component.hidePassword()).toBe(true);
  });

  it('does not start a second login while one is in flight', async () => {
    const { component, auth } = setup();
    let resolve!: () => void;
    auth.login.mockImplementation(() => new Promise<void>((r) => (resolve = r)));
    component.username = 'a';
    component.password = 'b';

    const first = component.onSubmit();
    const second = component.onSubmit();

    resolve();
    await Promise.all([first, second]);

    expect(auth.login).toHaveBeenCalledTimes(1);
  });

  it('clicking the password toggle button flips hidePassword', () => {
    const { fixture, component } = setup();
    const toggle = fixture.nativeElement.querySelector('.toggle-pw') as HTMLButtonElement;
    expect(toggle).not.toBeNull();
    expect(component.hidePassword()).toBe(true);

    toggle.click();
    fixture.detectChanges();
    expect(component.hidePassword()).toBe(false);

    toggle.click();
    fixture.detectChanges();
    expect(component.hidePassword()).toBe(true);
  });

  it('toggle button aria-label flips with hidePassword state', () => {
    const { fixture, component } = setup();
    const toggle = fixture.nativeElement.querySelector('.toggle-pw') as HTMLButtonElement;

    expect(toggle.getAttribute('aria-label')).toBe('Show password');

    component.hidePassword.set(false);
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-label')).toBe('Hide password');
  });
});

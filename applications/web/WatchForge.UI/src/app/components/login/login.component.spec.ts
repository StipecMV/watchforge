import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService, AuthUser } from '../../services/auth.service';
import { I18nService } from '../../i18n/i18n.service';

const USER: AuthUser = {
  userId: 2,
  username: 'User Two',
  role: 'standard',
  avatarId: 5,
  locale: 'sk',
};

function createFakeAuth(overrides: Record<string, unknown> = {}) {
  const currentUser = signal<AuthUser | null>(null);
  return {
    currentUser,
    login: vi.fn(() => of({ kind: 'ok', user: USER })),
    setPassword: vi.fn(() => {
      currentUser.set(USER);
      return of({ kind: 'ok', user: USER });
    }),
    logout: vi.fn(() => of(undefined)),
    restoreSession: vi.fn(() => of(null)),
    ...overrides,
  };
}

describe('LoginComponent (S6-2)', () => {
  let auth: ReturnType<typeof createFakeAuth>;

  beforeEach(async () => {
    auth = createFakeAuth();
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [{ provide: AuthService, useValue: auth }],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('sk');
  });

  it('zobrazí sign-in formulár po slovensky (default locale)', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.textContent).toContain('Prihlásenie');
    expect(el.querySelector('input[formcontrolname="username"]')).not.toBeNull();
    expect(el.querySelector('input[formcontrolname="password"]')).not.toBeNull();
    expect(el.textContent).toContain('Prihlásiť sa');
  });

  it('po prepnutí locale na en zobrazí anglické texty', () => {
    TestBed.inject(I18nService).setLocale('en');
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.textContent).toContain('Sign in');
    expect(el.textContent).toContain('Username');
  });

  it('pri zlých credentials zobrazí chybu a neprihlási', () => {
    auth.login = vi.fn(() => of({ kind: 'invalid-credentials' as const }));
    const fixture = TestBed.createComponent(LoginComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    comp.signinForm.setValue({ username: 'Admin', password: 'zle-heslo' });
    comp.onSignIn();
    fixture.detectChanges();

    expect(auth.login).toHaveBeenCalledWith('Admin', 'zle-heslo');
    expect(auth.currentUser()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Nesprávne meno alebo heslo');
  });

  it('pri 428 (heslo nenastavené) prepne do režimu nastavenia hesla s predvyplneným menom', () => {
    auth.login = vi.fn(() => of({ kind: 'password-not-set' as const, username: 'User Two' }));
    const fixture = TestBed.createComponent(LoginComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    comp.signinForm.setValue({ username: 'User Two', password: 'hocijake' });
    comp.onSignIn();
    fixture.detectChanges();

    expect(comp.mode).toBe('setup');
    expect(comp.setupUsername).toBe('User Two');
    expect(fixture.nativeElement.textContent).toContain('Vitajte! Nastavte si heslo');
  });

  it('nastavenie hesla: krátke heslo (< 8) → validačná hláška, API sa nevolá', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    comp.enterSetup('User Two');
    fixture.detectChanges();
    comp.setupForm.setValue({ newPassword: 'kratke', confirmPassword: 'kratke' });
    comp.onSetPassword();
    fixture.detectChanges();

    expect(auth.setPassword).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Heslo musí mať aspoň 8 znakov');
  });

  it('nastavenie hesla: nezhoda hesiel → hláška, API sa nevolá', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    comp.enterSetup('User Two');
    fixture.detectChanges();
    comp.setupForm.setValue({ newPassword: 'dostatočne-dlhé-1', confirmPassword: 'ine-heslo' });
    comp.onSetPassword();
    fixture.detectChanges();

    expect(auth.setPassword).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Heslá sa nezhodujú');
  });

  it('nastavenie hesla: platný vstup → volá setPassword a prihlási používateľa', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    comp.enterSetup('User Two');
    fixture.detectChanges();
    comp.setupForm.setValue({ newPassword: 'dostatočne-dlhé-1', confirmPassword: 'dostatočne-dlhé-1' });
    comp.onSetPassword();
    fixture.detectChanges();

    expect(auth.setPassword).toHaveBeenCalledWith('User Two', 'dostatočne-dlhé-1');
    expect(auth.currentUser()).toEqual(USER);
  });

  it('forgot režim: zobrazí info o admin reset a návrat na prihlásenie', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    comp.showForgot();
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Zabudnuté heslo');
    expect(el.textContent).toContain('administrátor');

    comp.backToSignIn();
    fixture.detectChanges();
    expect(comp.mode).toBe('signin');
    expect(el.textContent).toContain('Prihlásenie');
  });
});

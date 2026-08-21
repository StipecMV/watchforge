import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  AuthService,
  AuthUser,
  LoginOutcome,
  SetPasswordOutcome,
} from './auth.service';

const USER: AuthUser = {
  userId: 1,
  username: 'Admin',
  role: 'admin',
  avatarId: 3,
  locale: 'sk',
};

describe('AuthService (S6-2)', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    service.currentUser.set(null);
  });

  it('login pošle POST /api/v1/auth/login s credentials a vráti ok + user', () => {
    let outcome: LoginOutcome | undefined;
    service.login('Admin', 'secret').subscribe(o => (outcome = o));

    const req = http.expectOne('/api/v1/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'Admin', password: 'secret' });
    expect(req.request.withCredentials).toBe(true);
    req.flush(USER);

    expect(outcome).toEqual({ kind: 'ok', user: USER });
    expect(service.currentUser()).toEqual(USER);
  });

  it('login zmaže currentUser pri zlých credentials (401) → invalid-credentials', () => {
    service.currentUser.set(USER);
    let outcome: LoginOutcome | undefined;
    service.login('Admin', 'bad').subscribe(o => (outcome = o));

    const req = http.expectOne('/api/v1/auth/login');
    req.flush({ error: 'Invalid username or password.' }, {
      status: 401,
      statusText: 'Unauthorized',
    });

    expect(outcome).toEqual({ kind: 'invalid-credentials' });
    expect(service.currentUser()).toBeNull();
  });

  it('login mapuje 428 → password-not-set (prvé nastavenie hesla)', () => {
    let outcome: LoginOutcome | undefined;
    service.login('User Two', 'whatever').subscribe(o => (outcome = o));

    const req = http.expectOne('/api/v1/auth/login');
    req.flush({ error: 'Password not set. Use POST /auth/set-password first.' }, {
      status: 428,
      statusText: 'Precondition Required',
    });

    expect(outcome).toEqual({ kind: 'password-not-set', username: 'User Two' });
    expect(service.currentUser()).toBeNull();
  });

  it('setPassword pošle POST /api/v1/auth/set-password a nastaví currentUser', () => {
    let outcome: SetPasswordOutcome | undefined;
    service.setPassword('User Two', 'novy-heslo-123').subscribe(o => (outcome = o));

    const req = http.expectOne('/api/v1/auth/set-password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'User Two', newPassword: 'novy-heslo-123' });
    req.flush({ ...USER, username: 'User Two' });

    expect(outcome).toEqual({ kind: 'ok', user: { ...USER, username: 'User Two' } });
    expect(service.currentUser()).toEqual({ ...USER, username: 'User Two' });
  });

  it('setPassword mapuje 404 → user-not-found a 409 → password-already-set', () => {
    let notFound: SetPasswordOutcome | undefined;
    service.setPassword('Niekto', 'novy-heslo-123').subscribe(o => (notFound = o));
    http.expectOne('/api/v1/auth/set-password').flush(
      { error: 'User not found.' },
      { status: 404, statusText: 'Not Found' },
    );
    expect(notFound).toEqual({ kind: 'user-not-found' });

    let conflict: SetPasswordOutcome | undefined;
    service.setPassword('Admin', 'novy-heslo-123').subscribe(o => (conflict = o));
    http.expectOne('/api/v1/auth/set-password').flush(
      { error: 'Password already set. Use reset (admin) if forgotten.' },
      { status: 409, statusText: 'Conflict' },
    );
    expect(conflict).toEqual({ kind: 'password-already-set' });
  });

  it('logout pošle POST /api/v1/auth/logout a zmaže currentUser', () => {
    service.currentUser.set(USER);
    let done = false;
    service.logout().subscribe(() => (done = true));

    const req = http.expectOne('/api/v1/auth/logout');
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    req.flush({ ok: true });

    expect(done).toBe(true);
    expect(service.currentUser()).toBeNull();
  });

  it('restoreSession: GET /me s platnou session → currentUser nastavený', () => {
    let user: AuthUser | null | undefined;
    service.restoreSession().subscribe(u => (user = u));

    const req = http.expectOne('/api/v1/auth/me');
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBe(true);
    req.flush(USER);

    expect(user).toEqual(USER);
    expect(service.currentUser()).toEqual(USER);
  });

  it('restoreSession: 401 → null a currentUser ostane null (žiadna session)', () => {
    let user: AuthUser | null | undefined;
    service.restoreSession().subscribe(u => (user = u));

    http.expectOne('/api/v1/auth/me').flush(
      { error: 'Authentication required (session or X-Api-Token).' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(user).toBeNull();
    expect(service.currentUser()).toBeNull();
  });
});

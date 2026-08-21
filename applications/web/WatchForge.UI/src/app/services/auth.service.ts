import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';
import { API_BASE_URL } from './api.service';

export interface AuthUser {
  userId: number;
  username: string;
  role: 'admin' | 'standard' | string;
  avatarId: number;
  locale: string;
}

export type LoginOutcome =
  | { kind: 'ok'; user: AuthUser }
  | { kind: 'password-not-set'; username: string }
  | { kind: 'invalid-credentials' };

export type SetPasswordOutcome =
  | { kind: 'ok'; user: AuthUser }
  | { kind: 'user-not-found' }
  | { kind: 'password-already-set' }
  | { kind: 'invalid-password' };

/**
 * Autentifikácia web UI (S6-2, FR-12).
 *
 * - Login: fixný username + heslo (PBKDF2) → session cookie (withCredentials).
 * - Prvé nastavenie hesla: login vráti 428 → UI prepne na set-password.
 * - Forgot password = admin reset v DB (žiadny email server); UI len vysvetľuje.
 * - `currentUser` signal je jediný zdroj pravdy o stave session pre celý UI
 *   (AppComponent gate: null → login screen, inak dashboard).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly currentUser = signal<AuthUser | null>(null);

  private readonly base = `${API_BASE_URL}/api/v1/auth`;

  constructor(private readonly http: HttpClient) {}

  /** POST /api/v1/auth/login — prihlásenie; 428 → používateľ si ešte nenastavil heslo. */
  login(username: string, password: string): Observable<LoginOutcome> {
    return this.http
      .post<AuthUser>(`${this.base}/login`, { username, password }, { withCredentials: true })
      .pipe(
        map(user => {
          this.currentUser.set(user);
          return { kind: 'ok' as const, user };
        }),
        catchError(err => {
          if (err?.status === 428) {
            return of({ kind: 'password-not-set' as const, username });
          }
          this.currentUser.set(null);
          return of({ kind: 'invalid-credentials' as const });
        }),
      );
  }

  /** POST /api/v1/auth/set-password — prvé nastavenie hesla (hash bol prázdny). */
  setPassword(username: string, newPassword: string): Observable<SetPasswordOutcome> {
    return this.http
      .post<AuthUser>(
        `${this.base}/set-password`,
        { username, newPassword },
        { withCredentials: true },
      )
      .pipe(
        map(user => {
          this.currentUser.set(user);
          return { kind: 'ok' as const, user };
        }),
        catchError(err => {
          if (err?.status === 404) return of({ kind: 'user-not-found' as const });
          if (err?.status === 409) return of({ kind: 'password-already-set' as const });
          return of({ kind: 'invalid-password' as const });
        }),
      );
  }

  /** POST /api/v1/auth/logout — zrušenie session cookie. */
  logout(): Observable<void> {
    return this.http
      .post<{ ok: boolean }>(`${this.base}/logout`, {}, { withCredentials: true })
      .pipe(
        map(() => {
          this.currentUser.set(null);
        }),
      );
  }

  /**
   * GET /api/v1/auth/me — obnovenie session pri štarte aplikácie.
   * Platná session → currentUser; inak null (žiadna chyba pre UI).
   */
  restoreSession(): Observable<AuthUser | null> {
    return this.http
      .get<AuthUser>(`${this.base}/me`, { withCredentials: true })
      .pipe(
        map(user => {
          this.currentUser.set(user);
          return user;
        }),
        catchError(() => {
          this.currentUser.set(null);
          return of(null);
        }),
      );
  }
}

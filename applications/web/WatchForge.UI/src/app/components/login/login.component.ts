import { Component, EventEmitter, Output, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { TranslatePipe } from '../../i18n/translate.pipe';

export type LoginMode = 'signin' | 'setup' | 'forgot';

/**
 * Login screen (S6-2, FR-11/FR-12) — ADMIN login (obyčajný používateľ sa
 * neprihlasuje — UI je verejné pre live view + analýzy).
 *
 * Tri režimy:
 * - `signin` — meno + heslo; 428 z API → automatický prechod do `setup`.
 * - `setup` — prvé nastavenie hesla (hash bol prázdny v DB).
 * - `forgot` — vysvetlenie, že reset hesla robí iba administrátor.
 *
 * Úspešné prihlásenie nastaví `AuthService.currentUser` signal a emituje
 * `loggedIn` (AppComponent prepne na live view; settings sa odomknú).
 */
@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css'],
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  /** Admin sa prihlásil — AppComponent zobrazí live view (+ admin možnosti). */
  @Output() loggedIn = new EventEmitter<void>();
  /** Zrušenie prihlásenia (← späť na live view). */
  @Output() cancel = new EventEmitter<void>();

  mode: LoginMode = 'signin';
  setupUsername = '';
  errorKey: string | null = null;
  busy = false;
  showPassword = false;

  readonly signinForm = this.fb.nonNullable.group({
    username: ['', Validators.required],
    password: ['', Validators.required],
  });

  readonly setupForm = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required],
  });

  onSignIn(): void {
    this.errorKey = null;
    if (this.signinForm.invalid) return;

    const { username, password } = this.signinForm.getRawValue();
    this.busy = true;
    this.auth.login(username, password).subscribe(outcome => {
      this.busy = false;
      if (outcome.kind === 'ok') {
        this.loggedIn.emit(); // currentUser signal + notifikácia AppComponent
        return;
      }
      if (outcome.kind === 'password-not-set') {
        this.enterSetup(outcome.username);
        return;
      }
      this.errorKey = 'invalid-credentials';
    });
  }

  onSetPassword(): void {
    this.errorKey = null;
    const { newPassword, confirmPassword } = this.setupForm.getRawValue();

    if (this.setupForm.invalid || newPassword.length < 8) {
      this.errorKey = 'password-too-short';
      return;
    }
    if (newPassword !== confirmPassword) {
      this.errorKey = 'password-mismatch';
      return;
    }

    this.busy = true;
    this.auth.setPassword(this.setupUsername, newPassword).subscribe(outcome => {
      this.busy = false;
      if (outcome.kind === 'ok') {
        this.loggedIn.emit();
        return;
      }
      if (outcome.kind === 'password-already-set') {
        this.errorKey = 'password-already-set';
      } else if (outcome.kind === 'user-not-found') {
        this.errorKey = 'user-not-found';
      } else {
        this.errorKey = 'invalid-password';
      }
    });
  }

  enterSetup(username: string): void {
    this.setupUsername = username;
    this.errorKey = null;
    this.setupForm.reset();
    this.mode = 'setup';
  }

  showForgot(): void {
    this.errorKey = null;
    this.mode = 'forgot';
  }

  backToSignIn(): void {
    this.errorKey = null;
    this.mode = 'signin';
  }

  togglePassword(): void {
    this.showPassword = !this.showPassword;
  }
}

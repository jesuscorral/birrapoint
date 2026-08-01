import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import Keycloak from 'keycloak-js';
import { KeycloakHandoffComponent } from './keycloak-handoff.component';

describe('KeycloakHandoffComponent', () => {
  let fixture: ComponentFixture<KeycloakHandoffComponent>;
  let router: { navigateByUrl: jest.Mock };
  let keycloak: { authenticated: boolean; login: jest.Mock };

  function createComponent() {
    fixture = TestBed.createComponent(KeycloakHandoffComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    jest.useFakeTimers();
    router = { navigateByUrl: jest.fn() };
    keycloak = { authenticated: false, login: jest.fn() };

    TestBed.configureTestingModule({
      imports: [KeycloakHandoffComponent],
      providers: [
        { provide: Router, useValue: router },
        { provide: Keycloak, useValue: keycloak },
      ],
    });
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('routes straight to root without calling login() when an SSO session already exists', () => {
    keycloak.authenticated = true;
    createComponent();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
    expect(keycloak.login).not.toHaveBeenCalled();
  });

  it('starts the Keycloak login flow after a short delay when there is no session', () => {
    createComponent();

    expect(keycloak.login).not.toHaveBeenCalled();
    jest.advanceTimersByTime(1500);

    // redirectUri points straight at the app root — otherwise keycloak-js defaults to the current
    // URL and a successful login would bounce back through this same handoff screen a second time.
    expect(keycloak.login).toHaveBeenCalledWith({ redirectUri: window.location.origin + '/' });
  });

  it('lets the "Continuar" button trigger login immediately, bypassing the delay', () => {
    createComponent();

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();

    expect(keycloak.login).toHaveBeenCalledWith({ redirectUri: window.location.origin + '/' });
  });
});

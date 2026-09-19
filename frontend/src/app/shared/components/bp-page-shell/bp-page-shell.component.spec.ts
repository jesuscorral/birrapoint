import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';

import { BpTopbarComponent } from '../bp-topbar/bp-topbar.component';
import { BpPageShellComponent } from './bp-page-shell.component';

@Component({
  standalone: true,
  imports: [BpPageShellComponent],
  template: `
    <bp-page-shell [homeLink]="homeLink" [title]="title">
      <p>Projected content</p>
    </bp-page-shell>
  `,
})
class HostComponent {
  homeLink = '/organizer/dashboard';
  title = 'Test title';
}

@Component({
  standalone: true,
  imports: [BpPageShellComponent],
  template: `
    <bp-page-shell>
      <p>Default content</p>
    </bp-page-shell>
  `,
})
class DefaultsHostComponent {}

describe('BpPageShellComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([])],
    });
  });

  it('renders exactly one topbar header landmark and one main landmark', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('header').length).toBe(1);
    expect(fixture.nativeElement.querySelectorAll('main').length).toBe(1);
  });

  it('projects content into the page container inside the main landmark', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const main = fixture.nativeElement.querySelector('main') as HTMLElement;
    expect(main.textContent).toContain('Projected content');
    expect(main.querySelector('.page-container p')?.textContent).toBe('Projected content');
  });

  it('forwards homeLink and title to bp-topbar', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const topbar = fixture.debugElement.query(By.directive(BpTopbarComponent));
    expect(topbar.componentInstance.homeLink()).toBe('/organizer/dashboard');
    expect(topbar.componentInstance.title()).toBe('Test title');
  });

  it('defaults homeLink to /organizer/dashboard and title to empty when not provided', () => {
    const fixture = TestBed.createComponent(DefaultsHostComponent);
    fixture.detectChanges();

    const topbar = fixture.debugElement.query(By.directive(BpTopbarComponent));
    expect(topbar.componentInstance.homeLink()).toBe('/organizer/dashboard');
    expect(topbar.componentInstance.title()).toBe('');
  });
});

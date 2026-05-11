import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { CustomResponseDialogComponent } from './custom-response-dialog.component';
import { CustomResponse } from '../../core/models/token.model';

function setup(data: CustomResponse | null = null) {
  const dialogRef = { close: vi.fn() };
  TestBed.configureTestingModule({
    imports: [CustomResponseDialogComponent, NoopAnimationsModule],
    providers: [
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: MatDialogRef, useValue: dialogRef },
    ],
  });
  const fixture = TestBed.createComponent(CustomResponseDialogComponent);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, dialogRef };
}

describe('CustomResponseDialogComponent', () => {
  // ── initial state ────────────────────────────────────────────────────────

  it('defaults to statusCode 200 when no data provided', () => {
    const { component } = setup(null);
    expect(component.statusCode).toBe(200);
  });

  it('defaults to application/json contentType when no data provided', () => {
    const { component } = setup(null);
    expect(component.contentType).toBe('application/json');
  });

  it('initialises from existing custom response', () => {
    const cr: CustomResponse = {
      statusCode: 201,
      contentType: 'text/plain',
      body: 'created',
      headers: '{"X-Custom":"yes"}',
    };
    const { component } = setup(cr);
    expect(component.statusCode).toBe(201);
    expect(component.contentType).toBe('text/plain');
    expect(component.body).toBe('created');
    expect(component.headersControl.value).toBe('{"X-Custom":"yes"}');
  });

  // ── jsonValidator ────────────────────────────────────────────────────────

  it('headersControl is valid for empty string', () => {
    const { component } = setup();
    component.headersControl.setValue('');
    expect(component.headersControl.valid).toBe(true);
  });

  it('headersControl is valid for valid JSON object', () => {
    const { component } = setup();
    component.headersControl.setValue('{"X-Foo":"bar"}');
    expect(component.headersControl.valid).toBe(true);
  });

  it('headersControl is invalid for malformed JSON', () => {
    const { component } = setup();
    component.headersControl.setValue('{not-json}');
    expect(component.headersControl.hasError('invalidJson')).toBe(true);
  });

  // ── save ────────────────────────────────────────────────────────────────

  it('save closes dialog with action save and dto', () => {
    const { component, dialogRef } = setup();
    component.statusCode = 201;
    component.contentType = 'text/plain';
    component.body = 'hello';
    component.headersControl.setValue('{}');
    component.save();
    expect(dialogRef.close).toHaveBeenCalledWith({
      action: 'save',
      dto: { statusCode: 201, contentType: 'text/plain', body: 'hello', headers: '{}' },
    });
  });

  it('save uses empty headers as {}', () => {
    const { component, dialogRef } = setup();
    component.headersControl.setValue('');
    component.save();
    const call = dialogRef.close.mock.calls[0][0];
    expect(call.dto.headers).toBe('{}');
  });

  it('save uses null body when body is whitespace only', () => {
    const { component, dialogRef } = setup();
    component.body = '   ';
    component.headersControl.setValue('{}');
    component.save();
    const call = dialogRef.close.mock.calls[0][0];
    expect(call.dto.body).toBeNull();
  });

  it('save does not close when headersControl is invalid', () => {
    const { component, dialogRef } = setup();
    component.headersControl.setValue('{bad json}');
    component.save();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  // ── reset ────────────────────────────────────────────────────────────────

  it('reset closes dialog with action reset', () => {
    const cr: CustomResponse = {
      statusCode: 200,
      contentType: 'application/json',
      body: null,
      headers: '{}',
    };
    const { component, dialogRef } = setup(cr);
    component.reset();
    expect(dialogRef.close).toHaveBeenCalledWith({ action: 'reset' });
  });
});

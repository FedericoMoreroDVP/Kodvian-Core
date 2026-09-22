import { CurrencyPipe } from '@angular/common';
import { Component, Inject, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { ProyectosService } from '../../services/proyectos.service';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

import { LedgerContrato } from '../../models/proyectos.models';

@Component({
  selector: 'app-contrato-ledger-dialog',
  standalone: true,
  imports: [MatDialogModule, FormsModule, MatButtonModule, CurrencyPipe],
  templateUrl: './contrato-ledger-dialog.component.html',
  styleUrl: './contrato-ledger-dialog.component.scss'
})
export class ContratoLedgerDialogComponent {
  private readonly api = inject(ProyectosService);
  year = new Date().getFullYear(); loading = false; error = '';
  constructor(@Inject(MAT_DIALOG_DATA) public data: LedgerContrato) {}
  load(): void {
    if (this.year < 2000 || this.year > 2100) { this.error = 'Año inválido'; return; }
    this.loading = true; this.error = '';
    this.api.obtenerLedgerContrato(this.data.contractId, this.year).subscribe({ next: data => { this.data = data; this.loading = false; },
      error: () => { this.error = 'No se pudo cargar el resumen'; this.loading = false; } });
  }
}

import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, Inject, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';

import { AuthSessionService } from '../../../../core/auth/auth-session.service';
import {
  AsignacionDesarrolladorProyecto,
  ComprobanteArchivo,
  ContratoDesarrollador,
  ContratoDesarrolladorFormulario,
  DesarrolladorExterno,
  DesarrolladorFormulario,
  LedgerContrato,
  LookupItem,
  PagoDesarrollador,
  PagoDesarrolladorFormulario,
  ProyectoDetalle,
  ProyectoFormulario,
  ProyectoListado
} from '../../models/proyectos.models';
import { ProyectosService } from '../../services/proyectos.service';
import { ContratoDesarrolladorFormDialogComponent } from '../contrato-desarrollador-form-dialog/contrato-desarrollador-form-dialog.component';
import { ContratoLedgerDialogComponent } from '../contrato-ledger-dialog/contrato-ledger-dialog.component';
import { DesarrolladorFormDialogComponent } from '../desarrollador-form-dialog/desarrollador-form-dialog.component';
import { PagoDesarrolladorFormDialogComponent } from '../pago-desarrollador-form-dialog/pago-desarrollador-form-dialog.component';

interface DevelopersDialogData {
  project: ProyectoListado;
}

interface ContractTableRow {
  key: string;
  developerId: string;
  developerName: string;
  roleLabel: string;
  contract?: ContratoDesarrollador;
}

@Component({
  selector: 'app-proyecto-developers-dialog',
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatTableModule, MatButtonModule, MatFormFieldModule, MatSelectModule, CurrencyPipe, DatePipe],
  templateUrl: './proyecto-developers-dialog.component.html',
  styleUrl: './proyecto-developers-dialog.component.scss'
})
export class ProyectoDevelopersDialogComponent implements OnInit {
  private readonly proyectosService = inject(ProyectosService);
  private readonly authSession = inject(AuthSessionService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly assignmentColumns = ['developer', 'status', 'actions'];
  readonly contractColumns = ['developer', 'role', 'mode', 'amount', 'startDate', 'actions'];
  readonly paymentColumns = ['date', 'amount', 'period', 'reference', 'receipts', 'actions'];

  developers: DesarrolladorExterno[] = [];
  analysts: LookupItem[] = [];
  assignments: AsignacionDesarrolladorProyecto[] = [];
  contracts: ContratoDesarrollador[] = [];
  payments: PagoDesarrollador[] = [];
  selectedContract?: ContratoDesarrollador;
  selectedDeveloperId = '';
  selectedAnalystId = '';
  projectDetail?: ProyectoDetalle;

  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: DevelopersDialogData) {}

  ngOnInit(): void {
    this.loadLookups();
    this.loadProjectDetail();
    this.loadDevelopers();
    this.loadAssignments();
    if (this.canViewEconomics) {
      this.loadContracts();
    }
  }

  get canViewEconomics(): boolean {
    return this.authSession.user?.permissions.includes('finances.read') ?? false;
  }
  get projectCanceled(): boolean { return this.projectDetail?.status === 'Cancelado'; }

  get selectedAnalyst(): LookupItem | undefined {
    return this.analysts.find((x) => x.id === this.selectedAnalystId);
  }

  get contractRows(): ContractTableRow[] {
    const rows: ContractTableRow[] = this.contracts.map((contract) => ({
      key: contract.id,
      developerId: contract.developerId,
      developerName: contract.developerName,
      roleLabel: this.selectedAnalyst?.developerId === contract.developerId ? 'Analista a cargo' : 'Desarrollador',
      contract
    }));

    const analyst = this.selectedAnalyst;
    if (analyst?.developerId && !rows.some((row) => row.developerId === analyst.developerId)) {
      rows.unshift({
        key: `analyst-${analyst.developerId}`,
        developerId: analyst.developerId,
        developerName: analyst.name,
        roleLabel: 'Analista a cargo'
      });
    }

    return rows;
  }

  loadLookups(): void {
    this.proyectosService.obtenerLookups().subscribe({
      next: (data) => { this.analysts = data.responsibles; this.preserveCurrentAnalyst(); },
      error: () => this.snackBar.open('No se pudieron cargar los analistas', 'Cerrar', { duration: 3500 })
    });
  }

  loadProjectDetail(): void {
    this.proyectosService.obtenerDetalle(this.data.project.id).subscribe({
      next: (project) => {
        this.projectDetail = project;
        this.selectedAnalystId = project.responsibleId ?? '';
        this.data.project.responsibleId = project.responsibleId;
        this.data.project.responsibleName = project.responsibleName;
        this.preserveCurrentAnalyst();
      },
      error: () => this.snackBar.open('No se pudo cargar el detalle del proyecto', 'Cerrar', { duration: 3500 })
    });
  }

  loadDevelopers(): void {
    this.proyectosService.obtenerDesarrolladores().subscribe({
      next: (data) => this.developers = data,
      error: () => this.snackBar.open('No se pudieron cargar los desarrolladores', 'Cerrar', { duration: 3500 })
    });
  }

  private preserveCurrentAnalyst(): void {
    const project = this.projectDetail;
    if (project?.responsibleId && !this.analysts.some(a => a.id === project.responsibleId)) {
      this.analysts = [...this.analysts, { id: project.responsibleId, name: `${project.responsibleName ?? 'Responsable'} (asignación existente)` }];
    }
  }

  guardarAnalista(): void {
    if (!this.projectDetail) {
      this.snackBar.open('Esperá a que cargue el proyecto', 'Cerrar', { duration: 2500 });
      return;
    }

    const payload: ProyectoFormulario = {
      clientId: this.projectDetail.clientId,
      name: this.projectDetail.name,
      description: this.projectDetail.description,
      responsibleId: this.selectedAnalystId || null,
      status: this.projectDetail.status,
      priority: this.projectDetail.priority,
      startDate: this.projectDetail.startDate ?? null,
      estimatedDeliveryDate: this.projectDetail.estimatedDeliveryDate ?? null,
      closingDate: this.projectDetail.closingDate ?? null,
      budget: this.projectDetail.budget ?? null,
      progressPercentage: this.projectDetail.progressPercentage,
      isActive: this.projectDetail.isActive
    };

    this.proyectosService.actualizar(this.data.project.id, payload).subscribe({
      next: (project) => {
        this.projectDetail = project;
        this.selectedAnalystId = project.responsibleId ?? '';
        this.data.project.responsibleId = project.responsibleId;
        this.data.project.responsibleName = project.responsibleName;
        this.ensureSelectedAnalystInDevelopers();
        this.snackBar.open('Analista a cargo actualizado', 'Cerrar', { duration: 3000 });
      },
      error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo actualizar el analista a cargo', 'Cerrar', { duration: 3500 })
    });
  }

  loadContracts(): void {
    if (!this.canViewEconomics) {
      return;
    }

    this.proyectosService.obtenerContratosDesarrollador(this.data.project.id).subscribe({
      next: (data) => {
        this.contracts = data;
        if (this.selectedContract) {
          const current = data.find((x) => x.id === this.selectedContract?.id);
          this.selectedContract = current;
          if (current) {
            this.loadPayments(current.id);
          }
        }
      },
      error: () => this.snackBar.open('No se pudieron cargar los contratos', 'Cerrar', { duration: 3500 })
    });
  }

  loadAssignments(): void {
    this.proyectosService.obtenerAsignacionesDesarrollador(this.data.project.id).subscribe({
      next: (data) => this.assignments = data,
      error: () => this.snackBar.open('No se pudo cargar el equipo asignado', 'Cerrar', { duration: 3500 })
    });
  }

  asignarDesarrollador(): void {
    if (!this.selectedDeveloperId) {
      this.snackBar.open('Seleccioná un desarrollador', 'Cerrar', { duration: 2500 });
      return;
    }

    this.proyectosService.asignarDesarrollador(this.data.project.id, { developerId: this.selectedDeveloperId }).subscribe({
      next: () => {
        this.selectedDeveloperId = '';
        this.snackBar.open('Desarrollador asignado correctamente', 'Cerrar', { duration: 3000 });
        this.loadAssignments();
      },
      error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo asignar el desarrollador', 'Cerrar', { duration: 3500 })
    });
  }

  quitarAsignacion(assignment: AsignacionDesarrolladorProyecto): void {
    const confirmed = window.confirm(`Se quitará a ${assignment.developerName} del equipo operativo del proyecto. ¿Continuar?`);
    if (!confirmed) {
      return;
    }

    this.proyectosService.quitarAsignacionDesarrollador(assignment.id).subscribe({
      next: () => {
        this.snackBar.open('Asignación eliminada correctamente', 'Cerrar', { duration: 3000 });
        this.loadAssignments();
      },
      error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo eliminar la asignación', 'Cerrar', { duration: 3500 })
    });
  }

  loadPayments(contractId: string): void {
    this.proyectosService.obtenerPagosContrato(contractId).subscribe({
      next: (data) => this.payments = data,
      error: () => this.snackBar.open('No se pudieron cargar los pagos', 'Cerrar', { duration: 3500 })
    });
  }

  crearDesarrollador(): void {
    const ref = this.dialog.open(DesarrolladorFormDialogComponent, { width: '760px', maxWidth: 'calc(100vw - 32px)', maxHeight: 'calc(100vh - 32px)', autoFocus: false });
    ref.afterClosed().subscribe((payload?: DesarrolladorFormulario) => {
      if (!payload) return;

      this.proyectosService.crearDesarrollador(payload).subscribe({
        next: () => {
          this.snackBar.open('Desarrollador creado correctamente', 'Cerrar', { duration: 3000 });
          this.loadDevelopers();
        },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo crear el desarrollador', 'Cerrar', { duration: 3500 })
      });
    });
  }

  crearContrato(): void {
    if (this.projectCanceled) return;
    const ref = this.dialog.open(ContratoDesarrolladorFormDialogComponent, {
      width: '760px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100vh - 32px)',
      autoFocus: false,
      data: { developers: this.developers }
    });

    ref.afterClosed().subscribe((payload?: ContratoDesarrolladorFormulario) => {
      if (!payload) return;

      this.proyectosService.crearContratoDesarrollador(this.data.project.id, payload).subscribe({
        next: () => {
          this.snackBar.open('Contrato creado correctamente', 'Cerrar', { duration: 3000 });
          this.loadContracts();
        },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo crear el contrato', 'Cerrar', { duration: 3500 })
      });
    });
  }

  crearContratoPara(row: ContractTableRow): void {
    if (this.projectCanceled) return;
    this.ensureDeveloperInList(row.developerId, row.developerName);
    const ref = this.dialog.open(ContratoDesarrolladorFormDialogComponent, {
      width: '760px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100vh - 32px)',
      autoFocus: false,
      data: { developers: this.developers, initialDeveloperId: row.developerId }
    });

    ref.afterClosed().subscribe((payload?: ContratoDesarrolladorFormulario) => {
      if (!payload) return;

      this.proyectosService.crearContratoDesarrollador(this.data.project.id, payload).subscribe({
        next: () => {
          this.snackBar.open('Acuerdo creado correctamente', 'Cerrar', { duration: 3000 });
          this.loadContracts();
        },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo crear el acuerdo', 'Cerrar', { duration: 3500 })
      });
    });
  }

  seleccionarFilaContrato(row: ContractTableRow): void {
    if (row.contract) {
      this.seleccionarContrato(row.contract);
    }
  }

  verLedgerFila(row: ContractTableRow): void {
    if (row.contract) {
      this.verLedger(row.contract);
    }
  }

  registrarPagoFila(row: ContractTableRow): void {
    if (row.contract) {
      this.registrarPago(row.contract);
    }
  }

  editarContratoFila(row: ContractTableRow): void {
    if (row.contract) {
      this.editarContrato(row.contract);
      return;
    }

    this.crearContratoPara(row);
  }

  eliminarContrato(contract: ContratoDesarrollador): void {
    const confirmed = window.confirm(`Se eliminará el acuerdo de ${contract.developerName} y se anularán sus pagos y egresos financieros vinculados. Esta acción conserva el historial como anulado. ¿Continuar?`);
    if (!confirmed) return;
    this.proyectosService.eliminarContratoDesarrollador(contract.id).subscribe({
      next: () => {
        this.selectedContract = undefined; this.payments = [];
        this.snackBar.open('Acuerdo, pagos y egresos anulados correctamente', 'Cerrar', { duration: 3500 });
        this.loadContracts();
      },
      error: error => this.snackBar.open(error?.error?.message ?? 'No se pudo eliminar el acuerdo', 'Cerrar', { duration: 4000 })
    });
  }

  editarContrato(contract: ContratoDesarrollador): void {
    if (this.projectCanceled) return;
    const ref = this.dialog.open(ContratoDesarrolladorFormDialogComponent, {
      width: '760px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100vh - 32px)',
      autoFocus: false,
      data: { developers: this.developers, contract }
    });

    ref.afterClosed().subscribe((payload?: ContratoDesarrolladorFormulario) => {
      if (!payload) return;

      this.proyectosService.actualizarContratoDesarrollador(contract.id, payload).subscribe({
        next: () => {
          this.snackBar.open('Contrato actualizado correctamente', 'Cerrar', { duration: 3000 });
          this.loadContracts();
        },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo actualizar el contrato', 'Cerrar', { duration: 3500 })
      });
    });
  }

  seleccionarContrato(contract: ContratoDesarrollador): void {
    this.selectedContract = contract;
    this.loadPayments(contract.id);
  }

  registrarPago(contract: ContratoDesarrollador, payment?: PagoDesarrollador): void {
    if (this.projectCanceled || !contract.isActive) return;
    const ref = this.dialog.open(PagoDesarrolladorFormDialogComponent, { width: '860px', maxWidth: 'calc(100vw - 32px)', maxHeight: 'calc(100vh - 32px)', autoFocus: false, data: { contractId: contract.id, currency: contract.currency, payment } });
    ref.afterClosed().subscribe((changed?: boolean) => {
      if (changed) { this.selectedContract = contract; this.loadPayments(contract.id); this.loadContracts(); }
    });
  }
  editarPago(payment: PagoDesarrollador): void { if (!this.projectCanceled && this.selectedContract) this.registrarPago(this.selectedContract, payment); }
  anularPago(payment: PagoDesarrollador): void {
    if (!confirm('¿Anular este pago y su egreso financiero vinculado?')) return;
    this.proyectosService.anularPago(payment).subscribe({ next: () => this.loadPayments(payment.contractId),
      error: e => this.snackBar.open(e?.error?.message ?? 'No se pudo anular', 'Cerrar', { duration: 4000 }) });
  }

  verLedger(contract: ContratoDesarrollador): void {
    const year = new Date().getFullYear();
    this.proyectosService.obtenerLedgerContrato(contract.id, year).subscribe({
      next: (ledger: LedgerContrato) => {
        this.dialog.open(ContratoLedgerDialogComponent, {
          width: '980px',
          maxWidth: 'calc(100vw - 32px)',
          maxHeight: 'calc(100vh - 32px)',
          autoFocus: false,
          data: ledger
        });
      },
      error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo obtener el ledger', 'Cerrar', { duration: 3500 })
    });
  }

  descargarComprobante(paymentId: string, receipt: ComprobanteArchivo): void {
    const url = `/api/developer-payments/${paymentId}/receipts/${receipt.id}`;
    window.open(url, '_blank', 'noopener');
  }

  eliminarComprobante(paymentId: string, receipt: ComprobanteArchivo): void {
    const confirmed = window.confirm(`Se eliminará el comprobante ${receipt.fileName}. ¿Continuar?`);
    if (!confirmed) {
      return;
    }

    this.proyectosService.eliminarComprobantePago(paymentId, receipt.id).subscribe({
      next: () => {
        this.snackBar.open('Comprobante eliminado correctamente', 'Cerrar', { duration: 3000 });
        if (this.selectedContract) {
          this.loadPayments(this.selectedContract.id);
        }
      },
      error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo eliminar el comprobante', 'Cerrar', { duration: 3500 })
    });
  }

  private ensureSelectedAnalystInDevelopers(): void {
    const analyst = this.selectedAnalyst;
    if (analyst?.developerId) {
      this.ensureDeveloperInList(analyst.developerId, analyst.name);
    }
  }

  private ensureDeveloperInList(developerId: string, fullName: string): void {
    if (this.developers.some((x) => x.id === developerId)) {
      return;
    }

    this.developers = [...this.developers, { id: developerId, fullName, isActive: true }];
  }
}

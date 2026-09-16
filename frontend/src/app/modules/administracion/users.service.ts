import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs';
import { ApiResponse, PagedResult } from '../../shared/models/api.models';

export interface ManagedUser {
  id: string;
  fullName: string;
  email: string;
  isActive: boolean;
  roles: string[];
  developerId?: string;
  sessionVersion: string;
}

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  list(pageNumber: number, pageSize: number, search: string) {
    const params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize).set('search', search.trim());
    return this.http.get<ApiResponse<PagedResult<ManagedUser>>>('/api/users', { params }).pipe(map(r => r.data));
  }
  roles() { return this.http.get<ApiResponse<string[]>>('/api/users/roles').pipe(map(r => r.data)); }
  updateRoles(user: ManagedUser, roles: string[]) {
    return this.http.put<ApiResponse<ManagedUser>>(`/api/users/${user.id}/roles`, { roles, expectedVersion: user.sessionVersion }).pipe(map(r => r.data));
  }
}

export interface ApiResponse<T> {
  success: boolean;
  message: string;
  data: T;
}

export interface CurrentUser {
  id: string;
  fullName: string;
  email: string;
  role: string;
  roles: string[];
  developerId?: string;
  permissions: string[];
}

export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: CurrentUser;
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface LocationDto {
  id: string;
  name: string;
  timezone: string;
  address: string | null;
}

export interface RoomDto {
  id: string;
  name: string;
  capacity: number;
  floor: string | null;
  requiresApproval: boolean;
  checkinRequired: boolean;
  status: string;
}

@Injectable({ providedIn: 'root' })
export class RoomsService {
  private readonly http = inject(HttpClient);

  listLocations(): Promise<LocationDto[]> {
    return firstValueFrom(this.http.get<LocationDto[]>('/api/v1/locations'));
  }

  listRooms(locationId: string): Promise<RoomDto[]> {
    return firstValueFrom(this.http.get<RoomDto[]>(`/api/v1/locations/${locationId}/rooms`));
  }
}

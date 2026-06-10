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

export interface BookingDto {
  id: string;
  roomId: string;
  title: string;
  startAt: string;
  endAt: string;
  status: string;
  organizerName: string;
  isMine: boolean;
  seriesId: string | null;
  checkinRequired: boolean;
  setupNotes: string | null;
  participants: string[];
}

export interface CreateBookingRequest {
  roomId: string;
  title: string;
  startAt: string;
  endAt: string;
  attendeeCount?: number;
  setupNotes?: string | null;
  participants?: string[];
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

  listBookings(locationId: string, from: string, to: string): Promise<BookingDto[]> {
    return firstValueFrom(
      this.http.get<BookingDto[]>('/api/v1/bookings', { params: { locationId, from, to } }),
    );
  }

  createBooking(request: CreateBookingRequest): Promise<BookingDto> {
    return firstValueFrom(this.http.post<BookingDto>('/api/v1/bookings', request));
  }

  cancelBooking(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/v1/bookings/${id}/cancel`, {}));
  }
}

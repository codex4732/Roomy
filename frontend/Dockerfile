FROM node:24-alpine AS build
WORKDIR /source
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Serve member at /, admin and kiosk under subpaths (technical design §10).
RUN npx ng build member && \
    npx ng build admin --base-href /admin/ && \
    npx ng build kiosk --base-href /kiosk/

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /source/dist/member/browser /usr/share/nginx/html
COPY --from=build /source/dist/admin/browser /usr/share/nginx/html/admin
COPY --from=build /source/dist/kiosk/browser /usr/share/nginx/html/kiosk
EXPOSE 80

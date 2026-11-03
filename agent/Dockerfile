FROM golang:1.27.3-alpine AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN go test ./... && CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags="-s -w" -o /out/support360-agent ./cmd/agent

FROM alpine:3.22
RUN addgroup -S support360 && adduser -S -G support360 -h /var/lib/support360 support360 \
    && mkdir -p /etc/support360 /var/lib/support360 /var/cache/support360 \
    && chown -R support360:support360 /etc/support360 /var/lib/support360 /var/cache/support360
COPY --from=build /out/support360-agent /usr/local/bin/support360-agent
USER support360
ENTRYPOINT ["support360-agent"]

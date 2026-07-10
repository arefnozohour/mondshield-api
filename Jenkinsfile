// MondShield BACKEND — build & deploy Postgres + API to one Ubuntu server.
//
// TOPOLOGY: the Jenkins CONTROLLER can run anywhere (e.g. locally on Windows); the deploy runs on
// the UBUNTU SERVER, attached to Jenkins as an SSH build agent labelled `ubuntu-deploy` (change the
// label below to match your node). That agent needs Docker + the compose plugin. Checkout and
// `docker compose` all happen on the server, so nothing Docker-related runs on the controller.
//
// The .env that docker compose reads can come from EITHER of two sources, chosen by the
// ENV_SOURCE build parameter:
//   - credential (default): a Jenkins "Secret file" credential (id = ENV_CREDENTIAL_ID, default
//     `mondshield-api-env`) holding your filled-in .env. Secrets stay out of git.
//   - path: a .env you already keep on the server, at ENV_FILE_PATH (default
//     /opt/mondshield/api/.env). Nothing is stored in Jenkins.

pipeline {
  // Run on the Ubuntu server (SSH agent). Change the label to match your Jenkins node.
  agent { label 'ubuntu-deploy' }

  options {
    timestamps()
    disableConcurrentBuilds()
  }

  parameters {
    choice(
      name: 'ENV_SOURCE',
      choices: ['credential', 'path'],
      description: 'Where the .env comes from: a Jenkins "Secret file" credential, or an existing file on the agent/server.'
    )
    string(
      name: 'ENV_CREDENTIAL_ID',
      defaultValue: 'mondshield-api-env',
      description: 'Jenkins "Secret file" credential id holding the .env. Used only when ENV_SOURCE=credential.'
    )
    string(
      name: 'ENV_FILE_PATH',
      defaultValue: '/opt/mondshield/api/.env',
      description: 'Absolute path to an existing .env on the agent. Used only when ENV_SOURCE=path.'
    )
  }

  stages {
    stage('Checkout') {
      steps { checkout scm }
    }

    stage('Provide .env') {
      steps {
        script {
          if (params.ENV_SOURCE == 'credential') {
            withCredentials([file(credentialsId: params.ENV_CREDENTIAL_ID, variable: 'ENV_FILE')]) {
              sh 'cp "$ENV_FILE" .env'
            }
          } else {
            // Copy the server-side .env into the workspace so `docker compose` picks it up. Fail
            // loudly if it isn't there rather than deploying with missing config.
            sh '''
              if [ ! -f "$ENV_FILE_PATH" ]; then
                echo "ENV_SOURCE=path but no file at $ENV_FILE_PATH. Create it (from .env.example) or use ENV_SOURCE=credential." >&2
                exit 1
              fi
              cp "$ENV_FILE_PATH" .env
            '''
          }
        }
      }
    }

    stage('Build & deploy') {
      steps {
        // Bring Postgres up only if its container isn't already running — an existing DB is left
        // untouched (no recreate, no restart, no risk to data). Then always rebuild & update just
        // the API (`--no-deps` so it doesn't drag Postgres into a recreate).
        sh '''
          PG_ID="$(docker compose ps -q postgres 2>/dev/null || true)"
          if [ -n "$PG_ID" ] && [ "$(docker inspect -f '{{.State.Running}}' "$PG_ID" 2>/dev/null)" = "true" ]; then
            echo "Postgres container already running ($PG_ID) — skipping DB startup."
          else
            echo "Postgres not running — starting it and waiting for health."
            # --wait blocks until the healthcheck passes, so the API (started with --no-deps below,
            # which skips the depends_on health gate) never comes up against a not-ready DB.
            docker compose up -d --wait postgres
          fi

          docker compose up -d --build --no-deps --remove-orphans api
        '''
      }
    }
  }

  post {
    success { sh 'docker compose ps' }
    always  { sh 'docker image prune -f || true' }
  }
}
